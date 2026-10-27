#!/usr/bin/env bash
# Runs the Aurora tests against a temporary Aurora PostgreSQL cluster, which it creates with CloudFormation and then
# deletes. See README.md.
#
#   run.sh           create the cluster, or update it if it exists, run the tests, then delete it
#   run.sh --keep    the same, but keep the cluster for another run
#   run.sh --delete  delete the cluster
set -euo pipefail
cd "$(dirname "$0")"

export AWS_REGION="${AWS_REGION:-eu-west-3}"
export DBX_AURORA_CLUSTER="${DBX_AURORA_CLUSTER:-dbx-aurora-test}"

stack_events() {
  aws cloudformation describe-stack-events --stack-name "$1" --output text \
    --query "reverse(StackEvents)[].[EventId,Timestamp,LogicalResourceId,ResourceStatus,ResourceStatusReason]" \
    2>/dev/null || true
}

# Runs a command in the background and prints the stack's events as they happen, since CloudFormation's own commands
# print nothing while they wait. Returns the command's exit status.
with_stack_events() {
  local stack ids seen="#none#" new pid running

  # A deleted stack's events can be read only by its ID. A stack that doesn't exist yet goes by its name.
  stack=$(aws cloudformation describe-stacks --stack-name "$DBX_AURORA_CLUSTER" --query "Stacks[0].StackId" \
    --output text 2>/dev/null || echo "$DBX_AURORA_CLUSTER")

  # An empty pattern would match, and so hide, every event.
  ids=$(stack_events "$stack" | cut -f1)
  [ -z "$ids" ] || seen+=$'\n'"$ids"

  "$@" &
  pid=$!

  while true; do
    running=true
    kill -0 "$pid" 2>/dev/null || running=false

    new=$(stack_events "$stack" | grep -vF -- "$seen" || true)

    if [ -n "$new" ]; then
      awk -F'\t' '{ print substr($2, 12, 8) "  " $3 "  " $4 ($5 == "None" ? "" : "  " $5) }' <<< "$new"
      seen+=$'\n'"$(cut -f1 <<< "$new")"
    fi

    [ "$running" = true ] || break
    sleep 10
  done

  wait "$pid"
}

deploy_stack() {
  aws cloudformation deploy --stack-name "$DBX_AURORA_CLUSTER" --template-file aurora-test-cluster.yaml \
    --parameter-overrides "ClientCidr=$ip/32" --tags purpose=dbx-aurora-tests --no-fail-on-empty-changeset
}

delete_stack() {
  aws cloudformation delete-stack --stack-name "$DBX_AURORA_CLUSTER"
  aws cloudformation wait stack-delete-complete --stack-name "$DBX_AURORA_CLUSTER"
}

delete() {
  echo "Deleting stack $DBX_AURORA_CLUSTER in $AWS_REGION, which takes about 10 minutes."
  with_stack_events delete_stack
  echo "Deleted."
}

case "${1:-}" in
  "") keep=false ;;
  --keep) keep=true ;;
  --delete) delete; exit ;;
  *) echo "Usage: $0 [--keep | --delete]" >&2; exit 2 ;;
esac

ip=$(curl -fsS https://checkip.amazonaws.com | tr -d '[:space:]')

if [ "$keep" = false ]; then
  trap delete EXIT
fi

echo "Deploying stack $DBX_AURORA_CLUSTER in $AWS_REGION for $ip/32. A new cluster takes about 15 minutes."
with_stack_events deploy_stack

dotnet test --filter "FullyQualifiedName~DomainBlocks.EventStore.PostgreSQL.Tests.Aurora" \
  --logger "console;verbosity=normal"

if [ "$keep" = true ]; then
  echo "Kept stack $DBX_AURORA_CLUSTER in $AWS_REGION. Delete it with: $0 --delete"
fi
