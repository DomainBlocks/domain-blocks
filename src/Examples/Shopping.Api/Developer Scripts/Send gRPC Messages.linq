<Query Kind="Program">
  <Reference Relative="..\bin\Debug\net5.0\Shopping.Api.dll" />
  <NuGetReference>Grpc.Net.Client</NuGetReference>
  <Namespace>Grpc.Core</Namespace>
  <Namespace>Grpc.Core.Interceptors</Namespace>
  <Namespace>Grpc.Core.Utils</Namespace>
  <Namespace>Grpc.Net.Client</Namespace>
  <Namespace>Grpc.Net.Client.Configuration</Namespace>
  <Namespace>Grpc.Net.Compression</Namespace>
  <Namespace>Shopping.Api</Namespace>
  <Namespace>Shopping.Api.Services</Namespace>
  <Namespace>System.Threading.Tasks</Namespace>
</Query>

async Task Main()
{
	var channel = GrpcChannel.ForAddress("http://localhost:5000");
	
	var service = new Shopping.Api.ShoppingService.ShoppingServiceClient(channel);
	
	var subscription = service.Subscribe();
	
	var responseStream = subscription.ResponseStream;
	var requestStream = subscription.RequestStream;
	
	var responseTask = HandleResponse(responseStream);

	var cartId = Guid.NewGuid().ToString();
	var addItemRequest = new Request
	{
		AddItem = new AddItemToShoppingCart
		{
			CartId = cartId,
			ItemId = Guid.NewGuid().ToString(),
			ItemName = "First item"
		},
		Header = new RequestHeader
		{
			Identifier = new MessageIdentifier
			{
				CorrelationId = Guid.NewGuid().ToString()
			}
		}
	};

	var removeItemRequest = new Request
	{
		RemoveItem = new RemoveItemFromShoppingCart
		{
			CartId = cartId,
			ItemId = addItemRequest.AddItem.ItemId
		},
		Header = new RequestHeader
		{
			Identifier = new MessageIdentifier
			{
				CorrelationId = Guid.NewGuid().ToString()
			}
		}
	};

	$"Sending add item request. Correlation ID: {addItemRequest.Header.Identifier.CorrelationId}".Dump();
	await requestStream.WriteAsync(addItemRequest);

	$"Sending remove item request. Correlation ID: {removeItemRequest.Header.Identifier.CorrelationId}".Dump();
	await requestStream.WriteAsync(removeItemRequest);
	
	await requestStream.CompleteAsync();

	await responseTask;
	
}

private async Task HandleResponse(IAsyncStreamReader<Response> responseStream)
{
	await foreach (var response in responseStream.ReadAllAsync())
	{
		var text = $"Got response for message: {response.Header.Identifier.CorrelationId}. " +
				   $"Success {response.Body.CommandAck.Success}";
				   
		text.Dump();
	}
}

// You can define other methods, fields, classes and namespaces here
