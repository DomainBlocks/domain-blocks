using System;
using System.Threading.Tasks;
using Grpc.Core;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Shopping.Api.Services;

public class ShoppingService : Api.ShoppingService.ShoppingServiceBase
{
    private readonly ILogger<ShoppingService> _logger;
    private readonly IMediator _mediator;

    public ShoppingService(ILogger<ShoppingService> logger, IMediator mediator)
    {
        _logger = logger;
        _mediator = mediator;
    }

    public override async Task Subscribe(IAsyncStreamReader<Request> requestStream, IServerStreamWriter<Response> responseStream, ServerCallContext context)
    {
        while (await requestStream.MoveNext())
        {
            var request = requestStream.Current;
            CommandAcknowledgement ack;

            switch (request.CommandMessageCase)
            {
                case Request.CommandMessageOneofCase.AddItem:
                    _logger.LogInformation("Sending AddItemToShoppingCart command");
                    ack = await _mediator.Send(request.AddItem, context.CancellationToken);
                    break;
                case Request.CommandMessageOneofCase.RemoveItem:
                    _logger.LogInformation("Sending RemoveItemFromShoppingCart command");
                    ack = await _mediator.Send(request.RemoveItem, context.CancellationToken);
                    break;
                case Request.CommandMessageOneofCase.SaveForLater:
                    _logger.LogInformation("Sending SaveItemForLater command");
                    ack = await _mediator.Send(request.SaveForLater, context.CancellationToken);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            await responseStream.WriteAsync(new Response
            {
                Header = new ResponseHeader
                {
                    Identifier = request.Header.Identifier,
                },
                Body = new ResponseBody {CommandAck = ack}
            });
        }
    }
}