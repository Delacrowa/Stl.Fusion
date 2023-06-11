using Stl.Interception;

namespace Stl.Rpc.Infrastructure;

public sealed class RpcSystemCallSender : RpcServiceBase
{
    private IRpcSystemCalls? _client;
    private RpcServiceDef? _systemCallsServiceDef;
    private RpcMethodDef? _okMethodDef;
    private RpcMethodDef? _errorMethodDef;
    private RpcMethodDef? _cancelMethodDef;

    private IRpcSystemCalls Client => _client
        ??= Services.GetRequiredService<IRpcSystemCalls>();
    private RpcServiceDef SystemCallsServiceDef => _systemCallsServiceDef
        ??= Hub.ServiceRegistry.Get<IRpcSystemCalls>()!;
    private RpcMethodDef OkMethodDef => _okMethodDef
        ??= SystemCallsServiceDef.Methods.Single(m => Equals(m.Method.Name, nameof(IRpcSystemCalls.Ok)));
    private RpcMethodDef ErrorMethodDef => _errorMethodDef
        ??= SystemCallsServiceDef.Methods.Single(m => Equals(m.Method.Name, nameof(IRpcSystemCalls.Error)));
    private RpcMethodDef CancelMethodDef => _cancelMethodDef
        ??= SystemCallsServiceDef.Methods.Single(m => Equals(m.Method.Name, nameof(IRpcSystemCalls.Cancel)));

    public RpcSystemCallSender(IServiceProvider services) : base(services)
    { }

    public ValueTask Complete<TResult>(RpcPeer peer, long callId, Result<TResult> result, List<RpcHeader>? headers = null)
        => result.IsValue(out var value)
            ? Ok(peer, callId, value, headers)
            : Error(peer, callId, result.Error!, headers);

    public async ValueTask Ok<TResult>(RpcPeer peer, long callId, TResult result, List<RpcHeader>? headers = null)
    {
        var context = new RpcOutboundContext(headers) {
            Peer = peer,
            RelatedCallId = callId,
        };
        // An optimized version of Client.Ok(result):
        var call = context.SetCall(OkMethodDef, ArgumentList.New(result))!;
        await call.Send().ConfigureAwait(false);
    }

    public async ValueTask Error(RpcPeer peer, long callId, Exception error, List<RpcHeader>? headers = null)
    {
        var context = new RpcOutboundContext(headers) {
            Peer = peer,
            RelatedCallId = callId,
        };
        // An optimized version of Client.Error(result):
        var call = context.SetCall(ErrorMethodDef, ArgumentList.New(error.ToExceptionInfo()))!;
        await call.Send().ConfigureAwait(false);
    }

    public async ValueTask Cancel(RpcPeer peer, long callId, List<RpcHeader>? headers = null)
    {
        var context = new RpcOutboundContext(headers) {
            Peer = peer,
            RelatedCallId = callId,
        };
        // An optimized version of Client.Error(result):
        var call = context.SetCall(CancelMethodDef, ArgumentList.Empty)!;
        await call.Send().ConfigureAwait(false);
    }
}
