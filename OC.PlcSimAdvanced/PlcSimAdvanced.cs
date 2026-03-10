using Siemens.Simatic.Simulation.Runtime;
using OC.Assistant.Sdk;
using OC.Assistant.Sdk.Plugin;

namespace OC.PlcSimAdvanced;

[PluginDelayAfterStart(2000)]
public class PlcSimAdvanced : PluginBase
{
    [PluginParameter("Name of the PlcSim Advanced instance")]
    private readonly string _plcName = "PLC_1";
        
    [PluginParameter("Unique id for acyclic communication")]
    private readonly ushort _identifier = 1;
        
    [PluginParameter("CycleTime in ms")]
    private readonly int _cycleTime = 10;
    
    [PluginParameter("e.g. 0-1023 or 0,1,2 or a combination")]
    private readonly string _inputAddress = "0-1023";
        
    [PluginParameter("e.g. 0-1023 or 0,1,2 or a combination")]
    private readonly string _outputAddress = "0-1023";
        
    private IInstance? _instance;
    private readonly StopwatchEx _stopwatch = new ();
    private byte[] _inputArea = [];
    private byte[] _outputArea = [];
    private double _timeScaling = 1.0;
    private int[] _inputList = [];
    private int[] _outputList = [];

    protected override bool OnSave()
    {
        _inputList = _inputAddress.ToNumberList();
        _outputList = _outputAddress.ToNumberList();

        foreach (var address in _inputList)
        {
            InputStructure.AddVariable($"I{address}", ManagedType.Byte);
        }

        foreach (var address in _outputList)
        {
            OutputStructure.AddVariable($"Q{address}", ManagedType.Byte);
        }
        
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
        
        Logger.LogInfo(this, $"Connected to Plc '{_plcName}'");
        return true;
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
            if (Math.Abs(_timeScaling - TimeScaling) > 0.001)
            {
                Logger.LogInfo(this, $"ScaleFactor for Plc '{_plcName}' changed from {_timeScaling} to {TimeScaling}");
                _timeScaling = TimeScaling;
                _instance.ScaleFactor = _timeScaling;
            }

            //Ads read
            for (var i = 0; i < _inputList.Length; ++i) _inputArea[_inputList[i]] = InputBuffer[i];

            //Write Plc inputs
            _instance.InputArea.WriteBytes(0, (uint)_inputArea.Length, _inputArea);

            //Read Plc outputs
            _outputArea = _instance.OutputArea.ReadBytes(0, (uint)_outputArea.Length);

            //Ads write
            for (var i = 0; i < _outputList.Length; ++i) OutputBuffer[i] = _outputArea[_outputList[i]];

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
                _instance.OnDataRecordRead -= OnDataRecordRead;
                _instance.OnDataRecordWrite -= OnDataRecordWrite;
                _instance.OnOperatingStateChanged -= InstanceOnOperatingStateChanged;
                _instance.Dispose();
                _instance = null;
            }
            
            RecordDataServer.OnWriteRes -= OnWriteRes;
            RecordDataServer.OnReadRes -= OnReadRes;
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
            
            RecordDataServer.OnWriteRes += OnWriteRes;
            RecordDataServer.OnReadRes += OnReadRes;

            _instance.UpdateTagList();
            _instance.OnDataRecordRead += OnDataRecordRead;
            _instance.OnDataRecordWrite += OnDataRecordWrite;
            _instance.OnOperatingStateChanged += InstanceOnOperatingStateChanged;
            return true;
        }
        catch (Exception e)
        {
            Logger.LogError(this, e.Message);
            return false;
        }
    }
        
    private void InstanceOnOperatingStateChanged(IInstance sender, ERuntimeErrorCode errorCode, DateTime dateTime, EOperatingState prevState, EOperatingState operatingState)
    {
        Logger.LogInfo(this, $"Plc '{sender.Name}' changed from state '{prevState}' to '{operatingState}'");
    }

    private void OnDataRecordWrite(IInstance sender, ERuntimeErrorCode errorCode, DateTime dateTime, SDataRecord e)
    {
        Logger.LogInfo(this, $"WrRec  Identifier {_identifier}  HardwareId {e.Info.HardwareId}  Index {e.Info.RecordIdx}  Data {BitConverter
            .ToString(e.Data, 0, Math.Min(10, e.Data.Length))}", true);
        RecordDataServer.WriteReq(e.Info.ToRecordDataTelegram(_identifier, e.Data));
    }

    private void OnDataRecordRead(IInstance sender, ERuntimeErrorCode errorCode, DateTime dateTime, SDataRecordInfo e)
    {
        Logger.LogInfo(this, $"RdRec  Identifier {_identifier}  HardwareId {e.HardwareId}  Index {e.RecordIdx}", true);
        RecordDataServer.ReadReq(e.ToRecordDataTelegram(_identifier));
    }
        
    private void OnWriteRes(RecordDataTelegram e)
    {
        if (e.Identifier != _identifier) return;
        var d = e.ToSDataRecord();
        Logger.LogInfo(this, $"WrRes  Identifier {_identifier}  HardwareId {d.Info.HardwareId}  Index {d.Info.RecordIdx}", true);
        _instance?.WriteRecordDone(d.Info, 0);
    }

    private void OnReadRes(RecordDataTelegram e)
    {
        if (e.Identifier != _identifier) return;
        var d = e.ToSDataRecord();
        Logger.LogInfo(this, $"RdRes  Identifier {_identifier}  HardwareId {d.Info.HardwareId}  Index {d.Info.HardwareId}  Data {BitConverter
            .ToString(d.Data, 0, Math.Min(10, d.Data.Length))}", true);
        _instance?.ReadRecordDone(d.Info, d.Data, 0);
    }
}