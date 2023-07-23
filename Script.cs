
///Turret Alarm V2
///Created by Foltast
///Version: 2023.2.1

/// <summary>
        /// Can be changed by users
        /// </summary>
string detectionTriggerName = "DetectionTrigger"; // Name for timer to start on when target is detected
string lostTriggerName = "LostTrigger"; // Name for timer to start on when target is lost or destroyed

string lcdTag = "[TA LCD]"; //Tag for LCD panels intended for displaying information
string turretsTag = ""; //Tag for turrets. Must be <""> if you don't want to use certain turrets

// Set 'false' for disable delay or 'true' to enable. By default 'false'
bool detectionTriggerDelay = false;
bool lostTriggerDelay = false;

//Time for delays (in seconds)
float detectionTriggerDelayTime = 3f;
float lostTriggerDelayTime = 3f;

// Set 'false' for disable auto refreshing or 'true' to enable. By default 'true'
bool refreshEnabled = true;

//How often the script will refresh the block lists, 100 = 1 sec. By default 600
int checkRate = 600;

//Search turrets on subgrids. Can be set to TRUE ('1') or FALSE ('0')
int searchInMultipleGrids = 0;

//When enalbed, the enhanced search will be use the WeaponCore method of sorting targets
//It may help if you are experiencing problems with target detecting by WC blocks in standard mode
//WARNING: This mode is not support the using of turrets tags
//by default 'false'
bool enhancedSearchWCTargets = false;

//Mesh mode. Can be set to 'DISABLED' ('0'), 'TOWER' ('1'), 'BASE' ('2') where
//BASE is your main grid (where you want to recieve the info) and TOWER is remote grid with turrets
//by default set as '0'
int meshMode = 0;

// Keep unique id for every grid mesh!
string broadcastTag = "";

//Additional debug info
bool debugMode = false;

//LCD Panels settings
string lcdFontFamily = "Monospace";
float lcdFontSize = 0.8f;
Color lcdFontColor = new Color(255, 130, 0);
TextAlignment lcdFontAlignment = TextAlignment.CENTER;

/// <summary>
        /// Do not touch below this line
        /// </summary>
        /// -------------------------------------------------------------------- ///
IMyTimerBlock timerOnDetection;
IMyTimerBlock timerOnLost;
static WcPbApi api;

List<IWeapon> weapons = new List<IWeapon>();
Dictionary<MyDetectedEntityInfo, float> threats = new Dictionary<MyDetectedEntityInfo, float>();
IMyTextSurface[] lcds = new IMyTextSurface[0];

AlarmStatus currentStatus;
AlarmStatus previousStatus;
AlarmStatus lastBroadcastedStatus;

bool isWCUsed = false;
bool isInit = false;
bool isLCDenabled = true;

string version = "2023.2.1";
string refreshPass;

int currentCheckPass = 0;
int maxInstr = 0;

public Dictionary<string, Action> actions = new Dictionary<string, Action>();
public Dictionary<string, string> defaultSettings = new Dictionary<string, string>() {
    {"detectionTriggerName","DetectionTrigger"},
    {"lostTriggerName","LostTrigger"},
    {"lcdTag","[TA LCD]"},
    {"turretsTag",""},
    {"detectionTriggerDelay","false"},
    {"lostTriggerDelay","false"},
    {"detectionTriggerDelayTime","3"},
    {"lostTriggerDelayTime","3"},
    {"refreshEnabled","true"},
    {"checkRate","600"},
    {"searchInMultipleGrids","0"},
    {"enhancedSearchWCTargets","false"},
    {"meshMode","0"},
    {"broadcastTag",""},
    {"debugMode","false"}
};

string lastSettingsRawData;

public Dictionary<string, string> currentSettings = new Dictionary<string, string>();

IMyBroadcastListener myBroadcastListener;

public Program()
{
    Runtime.UpdateFrequency = UpdateFrequency.Update1;
    api = new WcPbApi();

    currentSettings = defaultSettings;

    if(!ReadSettings())
    {
        ResetSettings();
    }
    else
    {
        ApplySettings();
    }

    Initialize();

    actions = new Dictionary<string, Action>
    {
        {"refresh", Initialize},
        {"refresh switch", delegate{refreshEnabled = !refreshEnabled; } },
        {"lcd switch", delegate{isLCDenabled = !isLCDenabled; } },
        {"debug", delegate{debugMode = !debugMode; } },
        {"settings reset", ResetSettings }
    };

    if (meshMode > 0)
    {
        myBroadcastListener = IGC.RegisterBroadcastListener(broadcastTag);
    }
}

void Initialize()
{
    RefreshBlocks();
    isInit = true;
}

public void Main(string argument)
{
    Echo($"Turret Alarm v2 by Foltast\nVersion: {version}\n");

    ArgumentHandler(argument);

    if (!isWCUsed)
    {
        try
        {
            isWCUsed = api.Activate(Me);
        }
        catch (Exception ex)
        {
            Echo($"ERROR: {ex.Message}");
        }
    }

    if (refreshEnabled)
    {
        refreshPass = (currentCheckPass / 100 + 1).ToString();
        Echo($"Next refresh in: {refreshPass}");
        currentCheckPass--;

        if (currentCheckPass <= 0)
        {
            if(lastSettingsRawData != Me.CustomData)
            {
                ReadSettings();
                ApplySettings();
            }

            RefreshBlocks();
            currentCheckPass = checkRate;
        }
    }

    currentStatus = GetCurrentStatus();

    if (meshMode > 0)
    {
        Echo("MeshMode is enabled");

        if (meshMode == 2)
        {
            Echo("MeshMode: Base");
            MeshListner();
        }
        else
        {
            Echo("MeshMode: Tower");
            MeshSender();
        }
    }

    if (previousStatus == AlarmStatus.detected && currentStatus == AlarmStatus.idle)
        currentStatus = AlarmStatus.lost;

    UpdateLCDs();
    SwitchCurrentStatus();

    previousStatus = currentStatus;

    if (debugMode)
    {
        WriteDebug();
    }
}

private void MeshListner()
{
    while (myBroadcastListener.HasPendingMessage)
    {
        MyIGCMessage message = myBroadcastListener.AcceptMessage();

        if (message.Tag == broadcastTag)
        {
            lastBroadcastedStatus = (AlarmStatus)message.Data;

            if (lastBroadcastedStatus > currentStatus)
            {
                currentStatus = lastBroadcastedStatus;
            }
        }
    }
}

void MeshSender()
{
    if (previousStatus != currentStatus)
    {
        IGC.SendBroadcastMessage(broadcastTag, (int)currentStatus);
    }
}

void RefreshBlocks()
{
    CheckTriggers();
    SearchLCDs();
    SearchTurrets();
}

void ArgumentHandler(string arg)
{
    if (string.IsNullOrWhiteSpace(arg))
    {
        return;
    }

    if (actions.ContainsKey(arg))
    {
        actions[arg]();
    }
}

void WriteDebug()
{
    int instructions = Runtime.CurrentInstructionCount;

    if (maxInstr < instructions)
    {
        maxInstr = instructions;
    }

    Echo("\nDebug info"
        + "\ninstr lmt: " + Runtime.MaxInstructionCount.ToString()
        + "\ninstr cur: " + instructions
        + "\ninstr max: " + maxInstr
        + "\nturrets tag: " + turretsTag
        + "\ncurrent status: " + currentStatus
        + "\nprevious status: " + previousStatus
        + "\nlast broadcasted status: " + lastBroadcastedStatus
        + "\nturrets :" + weapons.Count
        + "\nlcds: " + lcds.Length.ToString()
        + "\ninit: " + isInit.ToString()
        + "\nblocks refreshing: " + refreshEnabled.ToString()
        + "\ncheck rate: " + checkRate.ToString()
        + "\ncurrent pass: " + currentCheckPass.ToString()
        );
}

bool IsHaveTriggers()
{
    if (timerOnDetection == null && timerOnLost == null)
        return false;
    return true;
}

AlarmStatus GetCurrentStatus()
{
    if (weapons.Count < 1)
    {
        if (meshMode == 2)
        {
            return lastBroadcastedStatus;
        }

        Echo("ERROR: No turrets detected");
        return AlarmStatus.error;
    }

    foreach(var weapon in weapons)
    {
        if(weapon.HaveTargets())
        {
            Echo("Status: Enemy detected");
            return AlarmStatus.detected;
        }
    }

    if(enhancedSearchWCTargets)
    {
        //Thanks to @Chuckination for the idea and the ready-made implementation

        threats.Clear();
        api.GetSortedThreats(Me, threats);

        if (threats.Count > 0)
        {
            return AlarmStatus.detected;
        }
    }

    Echo("Status: Searching targets");
    return AlarmStatus.idle;
}

void SwitchCurrentStatus()
{
    if (meshMode == 1)
    {
        Echo("WARNING: Local triggers are not used in tower mode");
        return;
    }

    if (!IsHaveTriggers())
    {
        Echo("ERROR: No triggers detected");
        currentStatus = AlarmStatus.error;
        return;
    }

    switch (currentStatus)
    {
        case AlarmStatus.detected:
            if (previousStatus == AlarmStatus.detected)
                break;

            if(timerOnLost != null)
            {
                timerOnLost.StopCountdown();
            }
            

            if (detectionTriggerDelay)
            {
                if (!timerOnDetection.IsCountingDown)
                {
                    timerOnDetection.StartCountdown();
                }
            }
            else
            {
                if (timerOnLost != null)
                {
                    timerOnLost.StopCountdown();
                }

                if(timerOnDetection != null)
                {
                    timerOnDetection.Trigger();
                }
            }
            break;
        case AlarmStatus.lost:
            if (lostTriggerDelay)
            {
                if (timerOnLost == null)
                {
                    return;
                }

                if (!timerOnLost.IsCountingDown)
                    timerOnLost.StartCountdown();
            }
            else
            {
                if (timerOnDetection != null)
                {
                    timerOnDetection.StopCountdown();
                }             

                if(timerOnLost != null)
                {
                    timerOnLost.Trigger();
                }               
            }

            break;
    }
}

void CheckTriggers()
{
    if (currentCheckPass <= 0)
    {
        if (timerOnDetection == null)
        {
            Echo($"WARNING: OnDetection Timer is null, trying to get compatible timer (with name {detectionTriggerName})");
            timerOnDetection = GridTerminalSystem.GetBlockWithName(detectionTriggerName) as IMyTimerBlock;
            if (timerOnDetection != null)
                timerOnDetection.TriggerDelay = detectionTriggerDelayTime;
            else
                Echo($"WARNING: there is no timer for OnDetection. Please add Timer with name {detectionTriggerName} and start this script again (Button RUN)");
        }

        if (timerOnLost == null)
        {
            Echo($"WARNING: OnLost Timer is null, trying to get compatible timer (with name {lostTriggerName})");
            timerOnLost = GridTerminalSystem.GetBlockWithName(lostTriggerName) as IMyTimerBlock;
            if (timerOnLost != null)
                timerOnLost.TriggerDelay = lostTriggerDelayTime;
            else
                Echo($"WARNING: there is no timer for OnLost. Please add Timer with name {lostTriggerName} and start this script again (Button RUN)");
        }
    }
}

void SearchTurrets()
{
    weapons.Clear();

    List<IMyLargeTurretBase> vanillaTurrets = new List<IMyLargeTurretBase>();
    List<IMyTerminalBlock> moddedTurrets = new List<IMyTerminalBlock>();
    List<IMyTurretControlBlock> customTurrets = new List<IMyTurretControlBlock>();

    List<MyDefinitionId> tempIds = new List<MyDefinitionId>();
    api.GetAllCoreTurrets(tempIds);
    List<string> defSubIds = new List<string>();
    tempIds.ForEach(x => defSubIds.Add(x.SubtypeName));

    if (searchInMultipleGrids == 0)
    {
        GridTerminalSystem.GetBlocksOfType(vanillaTurrets, b => b.CubeGrid == Me.CubeGrid);
        GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(
            moddedTurrets, b => b.CubeGrid == Me.CubeGrid &&
                defSubIds.Contains(b.BlockDefinition.SubtypeName));
        GridTerminalSystem.GetBlocksOfType(customTurrets, b => b.CubeGrid == Me.CubeGrid);
    }
    else
    {
        GridTerminalSystem.GetBlocksOfType(vanillaTurrets);
        GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(
            moddedTurrets, b => defSubIds.Contains(b.BlockDefinition.SubtypeName));
        GridTerminalSystem.GetBlocksOfType(customTurrets);
    }

    if (turretsTag != "")
    {
        vanillaTurrets = vanillaTurrets.Where(t => t.CustomName.StartsWith(turretsTag)).ToList();
        vanillaTurrets.AddRange(SearchGrouppedTurrets().ConvertAll(x => (IMyLargeTurretBase)x));
        vanillaTurrets = vanillaTurrets.Distinct().ToList();

        moddedTurrets = moddedTurrets.Where(t => t.CustomName.StartsWith(turretsTag)).ToList();
        moddedTurrets.AddRange(SearchGrouppedTurrets());
        moddedTurrets = moddedTurrets.Distinct().ToList();

        customTurrets = customTurrets.Where(t => t.CustomName.StartsWith(turretsTag)).ToList();
        customTurrets.AddRange(SearchGrouppedTurrets().ConvertAll(x => (IMyTurretControlBlock)x));
        customTurrets = customTurrets.Distinct().ToList();
    }

    foreach (var vanillaTurret in vanillaTurrets)
    {
        VanillaWeapon weapon = new VanillaWeapon(vanillaTurret);
        weapons.Add(weapon);
    }

    foreach (var moddedTurret in moddedTurrets)
    {
        ModdedWeapon weapon = new ModdedWeapon(moddedTurret);
        weapons.Add(weapon);
    }
}

List<IMyTerminalBlock> SearchGrouppedTurrets()
{
    List<IMyTerminalBlock> turrets = new List<IMyTerminalBlock>();
    List<IMyBlockGroup> groups = new List<IMyBlockGroup>();
    GridTerminalSystem.GetBlockGroups(groups, g => g.Name.StartsWith(turretsTag));

    foreach (var group in groups)
    {
        List<IMyTerminalBlock> turretsList = new List<IMyTerminalBlock>();
        group.GetBlocks(turretsList);
        turrets.AddRange(turretsList);
    }

    return turrets;
}

void UpdateLCDs()
{
    if (!isLCDenabled)
    {
        return;
    }

    StringBuilder stringBuilder = new StringBuilder();
    stringBuilder.Append("TURRET ALARM INFO PANEL\n\nV2 version: ");
    stringBuilder.Append(version);
    stringBuilder.Append("\n\nMeshMode: ");
    stringBuilder.Append((MeshMode)meshMode);
    stringBuilder.Append("\n\nCurrent Status: ");
    stringBuilder.Append(currentStatus.ToString().ToUpper());

    if(refreshEnabled)
    {
        stringBuilder.Append("\n\nRefresh in: ");
        stringBuilder.Append(currentCheckPass > 50 ? refreshPass : "progress");
    }
    else
    {
        stringBuilder.Append("\n\nRefresh is DISABLED");
    }

    string lcdText = stringBuilder.ToString();

    foreach (var lcd in lcds)
    {
        lcd.WriteText(lcdText);
    }
}

void ResetSettings()
{
    currentSettings = defaultSettings;
    WriteSettings();
}

bool ReadSettings()
{
    string[] dataLines = Me.CustomData.Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
    int linesMatches = 0;

    foreach(var line in dataLines)
    {
        string[] readyToReadData = line.Split(new char[] { '=' }, StringSplitOptions.None);

        if(readyToReadData.Length < 2)
        {
            continue;
        }

        string key = readyToReadData[0].Trim();

        if (currentSettings.ContainsKey(key))
        {
            if(string.IsNullOrWhiteSpace(readyToReadData[1]))
            {
                currentSettings[key] = "";
            }
            else
            {
                currentSettings[key] = readyToReadData[1].Trim();
            }
            linesMatches++;
        }
    }

    return linesMatches == defaultSettings.Count;
}

void WriteSettings()
{
    Me.CustomData = "";

    foreach(var setting in currentSettings)
    {
        Me.CustomData += $"{setting.Key}={setting.Value}\n";
    }
}

void ApplySettings()
{
    detectionTriggerName = currentSettings["detectionTriggerName"];
    lostTriggerName = currentSettings["lostTriggerName"];

    lcdTag = currentSettings["lcdTag"];
    turretsTag = currentSettings["turretsTag"];

    detectionTriggerDelay = bool.Parse(currentSettings["detectionTriggerDelay"]);
    lostTriggerDelay = bool.Parse(currentSettings["lostTriggerDelay"]);

    detectionTriggerDelayTime = float.Parse(currentSettings["detectionTriggerDelayTime"]);
    lostTriggerDelayTime = float.Parse(currentSettings["lostTriggerDelayTime"]);

    refreshEnabled = bool.Parse(currentSettings["refreshEnabled"]);
    checkRate = int.Parse(currentSettings["checkRate"]);
    searchInMultipleGrids = int.Parse(currentSettings["searchInMultipleGrids"]);

    enhancedSearchWCTargets = bool.Parse(currentSettings["enhancedSearchWCTargets"]);

    meshMode = int.Parse(currentSettings["meshMode"]);
    broadcastTag = currentSettings["broadcastTag"];
    debugMode = bool.Parse(currentSettings["debugMode"]);

    lastSettingsRawData = Me.CustomData;
}

void SearchLCDs()
{
    if (!isLCDenabled)
    {
        return;
    }

    List<IMyTerminalBlock> tmp_lcds = new List<IMyTerminalBlock>();
    GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(tmp_lcds, b => b.CubeGrid == Me.CubeGrid && ((b is IMyTextSurfaceProvider && (b as IMyTextSurfaceProvider).SurfaceCount > 0) || b is IMyTextSurface) && b.CustomName.StartsWith(lcdTag));

    lcds = new IMyTextSurface[tmp_lcds.Count];

    for (int i = tmp_lcds.Count; i-- > 0;)
    {
        if (tmp_lcds[i] is IMyTextSurfaceProvider)
        {
            bool cust_si = false;
            if (tmp_lcds[i].CustomName.Length > (lcdTag.Length + 2) && tmp_lcds[i].CustomName[lcdTag.Length] == '[' && tmp_lcds[i].CustomName[lcdTag.Length + 2] == ']')
            {
                int srf_idx = (int)tmp_lcds[i].CustomName[lcdTag.Length + 1] - 48;
                if ((cust_si = srf_idx > 0 && srf_idx < 10 && (tmp_lcds[i] as IMyTextSurfaceProvider).SurfaceCount > srf_idx)) lcds[i] = ((IMyTextSurfaceProvider)tmp_lcds[i]).GetSurface(srf_idx);
            }
            if (!cust_si) lcds[i] = ((IMyTextSurfaceProvider)tmp_lcds[i]).GetSurface(0);
        }
        else lcds[i] = (IMyTextSurface)tmp_lcds[i];

        lcds[i].ContentType = (ContentType)1;
        lcds[i].Font = lcdFontFamily;
        lcds[i].FontSize = lcdFontSize;
        lcds[i].FontColor = lcdFontColor;
        lcds[i].Alignment = lcdFontAlignment;
        lcds[i].ContentType = ContentType.TEXT_AND_IMAGE;
    }
}

public class WcPbApi
{
    Action<ICollection<MyDefinitionId>> getCoreWeapons;
    Action<ICollection<MyDefinitionId>> getCoreTurrets;
    Func<IMyTerminalBlock, int, MyDetectedEntityInfo> getWeaponTarget;
    Action<IMyTerminalBlock, IDictionary<MyDetectedEntityInfo, float>> getSortedThreats;

    public bool Activate(IMyTerminalBlock pbBlock)
    {
        var dict = pbBlock.GetProperty("WcPbAPI")?.As<IReadOnlyDictionary<string, Delegate>>().GetValue(pbBlock);
        if (dict == null) return false;
        return ApiAssign(dict);
    }

    public bool ApiAssign(IReadOnlyDictionary<string, Delegate> delegates)
    {
        if (delegates == null)
            return false;

        AssignMethod(delegates, "GetWeaponTarget", ref getWeaponTarget);
        AssignMethod(delegates, "GetCoreWeapons", ref getCoreWeapons);
        AssignMethod(delegates, "GetCoreTurrets", ref getCoreTurrets);
        AssignMethod(delegates, "GetSortedThreats", ref getSortedThreats);

        return true;
    }

    private void AssignMethod<T>(IReadOnlyDictionary<string, Delegate> delegates, string name, ref T field) where T : class
    {
        if (delegates == null)
        {
            field = null;
            return;
        }
        Delegate del;
        if (!delegates.TryGetValue(name, out del))
            throw new Exception($"{GetType().Name} :: Couldn't find {name} delegate of type {typeof(T)}");
        field = del as T;
        if (field == null)
            throw new Exception(
                $"{GetType().Name} :: Delegate {name} is not type {typeof(T)}, instead it's: {del.GetType()}");
    }

    public void GetAllCoreWeapons(ICollection<MyDefinitionId> collection) => getCoreWeapons?.Invoke(collection);

    public void GetAllCoreTurrets(ICollection<MyDefinitionId> collection) => getCoreTurrets?.Invoke(collection);

    public MyDetectedEntityInfo? GetWeaponTarget(IMyTerminalBlock weapon, int weaponId = 0) =>
    getWeaponTarget?.Invoke(weapon, weaponId);

    public void GetSortedThreats(IMyTerminalBlock pBlock, IDictionary<MyDetectedEntityInfo, float> collection) =>
    getSortedThreats?.Invoke(pBlock, collection);
}

public enum AlarmStatus
{
    idle,
    detected,
    lost,
    error
}

public enum MeshMode
{
    Disabled,
    Tower,
    Base
}

interface IWeapon
{
    bool HaveTargets();
}

class VanillaWeapon : IWeapon
{
    IMyLargeTurretBase turret;

    public VanillaWeapon(IMyLargeTurretBase turretBase)
    {
        turret = turretBase;
    }

    public bool HaveTargets()
    {
        return turret.IsWorking ? turret.IsShooting : false;
    }
}

class ModdedWeapon : IWeapon
{
    IMyTerminalBlock turret;
    public ModdedWeapon(IMyTerminalBlock turretBlock)
    {
        turret = turretBlock;
    }

    public bool HaveTargets()
    {
        MyDetectedEntityInfo? entity = api.GetWeaponTarget(turret, 0);

        return !entity.Value.IsEmpty();
    }
}