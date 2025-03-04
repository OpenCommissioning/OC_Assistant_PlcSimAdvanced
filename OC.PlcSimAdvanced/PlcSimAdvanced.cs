using System.Collections.Concurrent;
using Siemens.Simatic.Simulation.Runtime;
using OC.Assistant.Sdk;
using OC.Assistant.Sdk.Plugin;

namespace OC.PlcSimAdvanced;

[PluginIoType(IoType.Address)]
[PluginDelayAfterStart(2000)]
public class PlcSimAdvanced : PluginBase
{
    [PluginParameter("Name of the PlcSim Advanced instance")]
    private readonly string _plcName = "PLC_1";
        
    [PluginParameter("Unique id for acyclic communication")]
    private readonly int _identifier = 1;
        
    [PluginParameter("CycleTime in ms")]
    private readonly int _cycleTime = 10;
        
    private IInstance? _instance;
    private IRecordDataHandle? _recordDataHandle;
    private ConcurrentQueue<RecordData> _writeRes = new ();
    private ConcurrentQueue<RecordData> _readRes = new ();
    private readonly StopwatchEx _stopwatch = new ();
    private byte[] _inputArea = [];
    private byte[] _outputArea = [];
    private double _timeScaling = 1.0;
    private double _receivedTimeScaling = 1.0;

    protected override bool OnSave()
    {
        return true;
    }

    protected override bool OnStart()
    {
        _stopwatch.Restart();
        
        if (!WaitForConnection(CancellationToken))
        {
            Logger.LogWarning(this, $"Connecting to Plc '{_plcName}' aborted");
            return false;
        }

        if (_instance is null) return false;
        _inputArea = new byte[_instance.InputArea.AreaSize];
        _outputArea = new byte[_instance.OutputArea.AreaSize];
        _timeScaling = _instance.ScaleFactor;
        
        ApiLocal.Interface.TimeScalingChanged += ApiOnTimeScalingChanged;
        
        Logger.LogInfo(this, $"Connected to Plc '{_plcName}'");
        return true;
    }

    private void ApiOnTimeScalingChanged(double value)
    {
        _receivedTimeScaling = value;
    }

    protected override void OnUpdate()
    {
        if (_instance is null) return;
        _stopwatch.WaitUntil(_cycleTime);

        try
        {
            if (_instance.OperatingState != EOperatingState.Run)
            {
                //Wait a sec...
                Thread.Sleep(1000);
                    
                //Complete shutdown -> disconnect
                if (_instance.OperatingState == EOperatingState.InvalidOperatingState)
                {   
                    var size = OutputBuffer.Length;
                    Array.Copy(new byte[size], OutputBuffer, size);
                    CancellationRequest();
                    return;
                }
            }
            
            //Update ScaleFactor
            if (_timeScaling.DiffersFrom(_receivedTimeScaling))
            {
                Logger.LogInfo(this, $"ScaleFactor for Plc '{_plcName}' changed from {_timeScaling} to {_receivedTimeScaling}");
                _timeScaling = _receivedTimeScaling;
                _instance.ScaleFactor = _timeScaling;
            }

            //Ads read
            for (var i = 0; i < InputAddress.Length; ++i) _inputArea[InputAddress[i]] = InputBuffer[i];

            //Write Plc inputs
            _instance.InputArea.WriteBytes(0, (uint)_inputArea.Length, _inputArea);

            //Read Plc outputs
            _outputArea = _instance.OutputArea.ReadBytes(0, (uint)_outputArea.Length);

            //Ads write
            for (var i = 0; i < OutputAddress.Length; ++i) OutputBuffer[i] = _outputArea[OutputAddress[i]];

            //Send record data response if available
            if (_writeRes.TryDequeue(out var recordData))
            {
                _instance.WriteRecordDone(recordData.Info, 0);
                Logger.LogInfo(this, recordData.Message, true);
            }

            if (_readRes.TryDequeue(out recordData))
            {
                _instance.ReadRecordDone(recordData.Info, recordData.Data, 0);
                Logger.LogInfo(this, recordData.Message, true);
            }

            var elapsedMilliseconds = _stopwatch.ElapsedMilliseconds;
            if ((int)elapsedMilliseconds > _cycleTime * 2)
            {
                Logger.LogWarning(this, $"CycleTime of Plc '{_plcName}' exceeded. DeltaTime = {(int)elapsedMilliseconds} ms");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(this, ex.Message, true);
            Thread.Sleep(200);
        }
    }

    protected override void OnStop()
    {
        try
        {
            if (_instance is not null)
            {
                _instance.OnDataRecordRead -= Instance_OnDataRecordRead;
                _instance.OnDataRecordWrite -= Instance_OnDataRecordWrite;
                _instance.OnOperatingStateChanged -= Instance_OnOperatingStateChanged;
                _instance.Dispose();
                _instance = null;
            }
            
            if (_recordDataHandle is not null)
            {
                _recordDataHandle.OnWriteRes -= AdsServer_OnAdsWriteCon;
                _recordDataHandle.OnReadRes -= AdsServer_OnAdsReadCon;
            }

            ApiLocal.Interface.TimeScalingChanged -= ApiOnTimeScalingChanged;
        }
        catch (Exception ex)
        {
            Logger.LogError(this, ex.Message, true);
        }
            
        Logger.LogInfo(this, $"Disconnected from Plc '{_plcName}'");
    }
        
    private bool WaitForConnection(CancellationToken token)
    {
        if (!SimulationRuntimeManager.IsRuntimeManagerAvailable)
        {
            Logger.LogError(this,"PlcSim Advanced not available");
            return false;
        }
            
        Logger.LogInfo(this, $"Trying to start and connect Plc '{_plcName}'...");

        try
        {
            _instance = SimulationRuntimeManager.CreateInterface(_plcName);
            _instance.PowerOn();

            while (_instance.OperatingState != EOperatingState.Run && !token.IsCancellationRequested)
            {
                if (_instance.OperatingState == EOperatingState.ShuttingDown)
                {
                    return false;
                }

                if (_stopwatch.ElapsedMilliseconds > 5000) return false;
                Thread.Sleep(100);
            }

            if (_instance.OperatingState != EOperatingState.Run)
            {
                return false;
            }
                
            _writeRes = new ConcurrentQueue<RecordData>();
            _readRes = new ConcurrentQueue<RecordData>();
            _recordDataHandle = RecordDataHandle.Instance;
            _recordDataHandle.OnWriteRes += AdsServer_OnAdsWriteCon;
            _recordDataHandle.OnReadRes += AdsServer_OnAdsReadCon;

            _instance.UpdateTagList();
            _instance.OnDataRecordRead += Instance_OnDataRecordRead;
            _instance.OnDataRecordWrite += Instance_OnDataRecordWrite;
            _instance.OnOperatingStateChanged += Instance_OnOperatingStateChanged;
            return true;
        }
        catch (Exception e)
        {
            Logger.LogError(this, e.Message);
            return false;
        }
    }
        
    private void Instance_OnOperatingStateChanged(IInstance sender, ERuntimeErrorCode errorCode, DateTime dateTime, EOperatingState prevState, EOperatingState operatingState)
    {
        Logger.LogInfo(this, $"Plc '{sender.Name}' changed from state '{prevState}' to '{operatingState}'");
    }

    private void Instance_OnDataRecordWrite(IInstance sender, ERuntimeErrorCode errorCode, DateTime dateTime, SDataRecord dataRecord)
    {
        var recordData = new RecordData(dataRecord, _identifier);
        Logger.LogInfo(this, recordData.Message, true);
        _recordDataHandle?.AddWriteReq(recordData);
    }

    private void Instance_OnDataRecordRead(IInstance sender, ERuntimeErrorCode errorCode, DateTime dateTime, SDataRecordInfo dataRecordInfo)
    {
        var recordData = new RecordData(dataRecordInfo, _identifier);
        Logger.LogInfo(this, recordData.Message, true);
        _recordDataHandle?.AddReadReq(recordData);
    }
        
    private void AdsServer_OnAdsWriteCon(AdsServer.Response e)
    {
        var expectedResult = 0x80000000 + (ushort)_identifier;
        if ((uint)e.Result != expectedResult) return; //WriteRes is not for this plc instance
        var recordData = new RecordData(e, _identifier);
        _writeRes.Enqueue(recordData);
    }

    private void AdsServer_OnAdsReadCon(AdsServer.Response e)
    {
        var expectedResult = 0x80000000 + (ushort)_identifier;
        if ((uint)e.Result != expectedResult) return; //ReadRes is not for this plc instance
        var recordData = new RecordData(e, _identifier);
        _readRes.Enqueue(recordData);
    }
}