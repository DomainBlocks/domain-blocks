<Query Kind="Program">
  <Reference Relative="..\bin\Debug\net5.0\Shopping.Api.dll">C:\Dev\Libraries\domain-lib\src\Examples\Shopping.Api\bin\Debug\net5.0\Shopping.Api.dll</Reference>
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
	var addItemRequest1 = new Request
	{
		AddItem = new AddItemToShoppingCart
		{
			CartId = cartId,
			ItemId = Guid.NewGuid().ToString(),
			ItemName = "First item"
		},
		Header = CreateHeaderWithCorrelationId()
	};

	var addItemRequest2 = new Request
	{
		AddItem = new AddItemToShoppingCart
		{
			CartId = cartId,
			ItemId = Guid.NewGuid().ToString(),
			ItemName = "Second item"
		},
		Header = CreateHeaderWithCorrelationId()
	};

	var removeItemRequest = new Request
	{
		RemoveItem = new RemoveItemFromShoppingCart
		{
			CartId = cartId,
			ItemId = addItemRequest1.AddItem.ItemId
		},
		Header = CreateHeaderWithCorrelationId()
	};

	$"Sending add first item request. Correlation ID: {addItemRequest1.Header.Identifier.CorrelationId}".Dump();
	await requestStream.WriteAsync(addItemRequest1);

	$"Sending add second item request. Correlation ID: {addItemRequest2.Header.Identifier.CorrelationId}".Dump();
	await requestStream.WriteAsync(addItemRequest2);

	$"Sending remove item request. Correlation ID: {removeItemRequest.Header.Identifier.CorrelationId}".Dump();
	await requestStream.WriteAsync(removeItemRequest);
	
	await requestStream.CompleteAsync();

	await responseTask;
	
}

private RequestHeader CreateHeaderWithCorrelationId()
{
	return new RequestHeader
	{
		Identifier = new MessageIdentifier
		{
			CorrelationId = Guid.NewGuid().ToString()
		}
	};
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
