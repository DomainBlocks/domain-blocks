using System;
using System.Threading;
using System.Threading.Tasks;
using DomainBlocks.Aggregates;
using DomainBlocks.Persistence;
using MediatR;
using Shopping.Domain.Events;

namespace Shopping.Api.CommandHandlers
{
    public abstract class CommandHandlerBase<TRequest> : IRequestHandler<TRequest, CommandAcknowledgement> where TRequest : IRequest<CommandAcknowledgement>
    {
        protected IAggregateRepository<IDomainEvent> Repository { get; }
        protected CommandDispatcher<object, IDomainEvent> CommandDispatcher { get; }

        protected CommandHandlerBase(IAggregateRepository<IDomainEvent> repository, CommandDispatcher<object, IDomainEvent> commandDispatcher)
        {
            Repository = repository;
            CommandDispatcher = commandDispatcher;
        }

        public async Task<CommandAcknowledgement> Handle(TRequest request, CancellationToken cancellationToken)
        {
            try
            {
                await HandleImpl(request, cancellationToken);

                return new CommandAcknowledgement
                {
                    Success = true
                };
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                return new CommandAcknowledgement
                {
                    Success = false
                };
            }
        }

        protected abstract Task HandleImpl(TRequest request, CancellationToken cancellationToken);
    }
}