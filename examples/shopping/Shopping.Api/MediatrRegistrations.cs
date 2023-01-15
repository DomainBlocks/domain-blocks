using MediatR;

namespace Shopping.Api;

// To be used in Mediatr request handlers, we need to decorate the generated gRPC types
// with the IRequest marker interface 
public sealed partial class AddItemToShoppingCart : IRequest<CommandAcknowledgement> { }
public sealed partial class RemoveItemFromShoppingCart : IRequest<CommandAcknowledgement> { }
public sealed partial class SaveItemForLater : IRequest<CommandAcknowledgement> { }