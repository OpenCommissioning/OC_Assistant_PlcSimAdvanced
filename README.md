# Open Commissioning Assistant Plugin for PLCSIM Advanced

### Description
Connects to a PLCSIM Advanced PLC instance using cyclic I/O and acyclic data communication.

### Quick Getting Started
- Download the zip file from the latest release page
- Unpack and place it in the directory or a subdirectory of the `OC.Assistant.exe`
- Start the Assistant and connect or create a TwinCAT solution
- Add a new plugin instance using the `+` button 
- Select `PlcSimAdvanced`, configure parameters and press `Apply` ([see also](https://github.com/OpenCommissioning/OC_Assistant?tab=readme-ov-file#installation-1))
- Depending on the parameters, a TwinCAT GVL with PLC In- and Outputs is generated  
- The plugin starts when TwinCAT goes to Run Mode and tries to connect to the PLC instance

### Plugin Parameters
- _AutoStart_: Automatic start and stop with TwinCAT
- _PlcName_: Name of the PLCSIM Advanced PLC instance
- _Identifier_: Unique id for acyclic communication
- _CycleTime_: CycleTime in ms
- _InputAddress_: Used PLC input range (e.g. 0-1023 or 0,1,2 or a combination)
- _OutputAddress_: Used PLC output range (e.g. 0-1023 or 0,1,2 or a combination)

### Requirements
To run the plugin, you need PLCSIM Advanced installed on your system with a valid license.\
See Siemens documentation how to create and start a PLC instance using PLCSIM Advanced.

To build the plugin yourself, you need to copy the 
`Siemens.Simatic.Simulation.Runtime.Api.x64.dll` to the project directory.\
You can find the dll in `C:\Program Files (x86)\Common Files\Siemens\PLCSIMADV\API\<YourVersion>\` by default.

> [!NOTE] 
> The plugin has been tested with PLCSIM Advanced Version `v4 SP1`, `v6` and `v6 Update1`
