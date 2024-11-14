using OC.Assistant.Sdk;
using TwinCAT.Ads;

namespace OC.PlcSimAdvanced;

public class AdsServer()
    : TwinCAT.Ads.Server.AdsServer("Open Commissioning AdsServer for PlcSimAdvanced")
{
    private readonly AmsAddress _plcAddress = new(851);
    private readonly TcRecordDataList _tcRecordDataList = new();

    public struct Response(uint invokeId, AdsErrorCode result, uint length, byte[]? data)
    {
        public readonly uint InvokeId = invokeId;
        public readonly AdsErrorCode Result = result;
        public readonly uint CbLength = length;
        public readonly byte[]? Data = data;
    }

    public void AdsWriteReq(uint invokeId, uint indexGroup, uint indexOffset, uint cbLength, byte[] data)
    {
        if (_tcRecordDataList.Contains(indexOffset))
        {
            WriteRequest(_plcAddress, invokeId, indexGroup, indexOffset, new ReadOnlySpan<byte>(data).Slice(0, (int)cbLength));
        }
    }
    
    public void AdsReadReq(uint invokeId, uint indexGroup, uint indexOffset, uint cbLength)
    {
        if (_tcRecordDataList.Contains(indexOffset))
        {
            ReadRequest(_plcAddress, invokeId, indexGroup, indexOffset, (int)cbLength);
        }
    }
    
    /// <inheritdoc />
    protected override Task<AdsErrorCode> OnWriteConfirmationAsync(AmsAddress rAddr, uint invokeId, AdsErrorCode result, CancellationToken cancel)
    {
        return Task.Run(() =>
        {
            OnAdsWriteCon?.Invoke(new Response(invokeId, result, 0, null));
            return AdsErrorCode.NoError;
        }, cancel);
    }

    /// <inheritdoc />
    protected override Task<AdsErrorCode> OnReadConfirmationAsync(AmsAddress rAddr, uint invokeId, AdsErrorCode result, ReadOnlyMemory<byte> data, CancellationToken cancel)
    {
        return Task.Run(() =>
        {
            OnAdsReadCon?.Invoke(new Response(invokeId, result, (uint)data.Length, data.ToArray()));
            return AdsErrorCode.NoError;
        }, cancel);
    }
    
    public event Action<Response>? OnAdsReadCon;
    public event Action<Response>? OnAdsWriteCon;
}