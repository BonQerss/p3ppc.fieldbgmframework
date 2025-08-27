using System;
using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using p3ppc.fieldbgmframework.Configuration;
using p3ppc.fieldbgmframework.Template;
using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.ReloadedII.Interfaces;
using Reloaded.Mod.Interfaces;
using Reloaded.Mod.Interfaces.Internal;

using p3ppc.expShare.NuGet.templates.defaultPlus;
using p3ppc.expShare;

using Reloaded.Mod.Interfaces;
using Reloaded.Hooks.Definitions;
using Reloaded.Memory;
using Reloaded.Memory.Interfaces;

namespace p3ppc.fieldbgmframework
{
    public class BgmTable
    {
        public int Adx { get; set; } = -1;
        public int MajorId { get; set; } = -1;
        public int MinorId { get; set; } = -1;
        public int TartarusFloor { get; set; } = -1;
        public int Time { get; set; } = -1; // 0=Morning, 1=Afternoon, 2=Evening, 5=Night
        public bool FemcOnly { get; set; } = false;
        public bool MaleOnly { get; set; } = false;
        public int Flag { get; set; } = -1;
        public int StartMonth { get; set; } = 1;
        public int StartDay { get; set; } = 1;
        public int EndMonth { get; set; } = 12;
        public int EndDay { get; set; } = 31;

        public static List<BgmTable> LoadFromJson(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    Console.WriteLine($"[BGM DEBUG] File does not exist: {filePath}");
                    return new List<BgmTable>();
                }

                var json = File.ReadAllText(filePath);
                Console.WriteLine($"[BGM DEBUG] JSON content length: {json.Length}");
                Console.WriteLine($"[BGM DEBUG] JSON content: {json}");

                var result = JsonSerializer.Deserialize<List<BgmTable>>(json) ?? new List<BgmTable>();
                Console.WriteLine($"[BGM DEBUG] Loaded {result.Count} BGM entries from {filePath}");

                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BGM DEBUG] Error loading JSON from {filePath}: {ex.Message}");
                Console.WriteLine($"[BGM DEBUG] Stack trace: {ex.StackTrace}");
                return new List<BgmTable>();
            }
        }

        public static List<BgmTable> SortBgmTable(List<BgmTable> bgmList)
        {
            return bgmList.ToList();
        }

        public override string ToString()
        {
            return $"BGM: Adx={Adx}, Field={MajorId}_{MinorId}, Floor={TartarusFloor}, Time={Time}, Femc={FemcOnly}, Male={MaleOnly}, Flag={Flag}, Date={StartMonth}/{StartDay}-{EndMonth}/{EndDay}";
        }
    }

    public unsafe class Mod : ModBase
    {
        private delegate nuint FieldBGMDelegate(int fieldMajor, int fieldMinor, int tartarusFloor);
        private delegate void MapBGMDelegate(TaskStruct* task);
        private delegate nuint PlayBGM2Delegate(int BGMid, int param2);
        private delegate nuint PlayBGMDelegate(int BGMid);
        private delegate nuint TimeOFDayDelegate();
        private delegate bool IsFemcDelegate();
        private delegate bool BitCheckDelegate(int flagIndex);
        private delegate int GetTotalDayDelegate();

        private readonly IModLoader _modLoader;
        private readonly Reloaded.Hooks.Definitions.IReloadedHooks? _hooks;
        private readonly ILogger _logger;
        private readonly IMod _owner;
        private Config _configuration;
        private readonly IModConfig _modConfig;

        private IHook<FieldBGMDelegate> _FieldBGMHook;
        private PlayBGMDelegate _BGMPlay;
        private PlayBGM2Delegate _BGMPlay2;
        private IsFemcDelegate _IsFemc;
        private TimeOFDayDelegate _TimeofDay;
        private BitCheckDelegate _BitCheck;
        private GetTotalDayDelegate _GetTotalDay;

        public List<List<BgmTable>> allBgmLists = new List<List<BgmTable>>();
        public List<BgmTable> FinalBGMList = new List<BgmTable>();

        private static nint CurrentBGMCueIDAddr;

        private struct TaskStruct
        {
        }

        public Mod(ModContext context)
        {
            _modLoader = context.ModLoader;
            _hooks = context.Hooks;
            _logger = context.Logger;
            _owner = context.Owner;
            _configuration = context.Configuration;
            _modConfig = context.ModConfig;

            try
            {
                Utils.Initialise(_logger, _configuration, _modLoader);
            }
            catch (Exception ex)
            {
                _logger.WriteLine($"Failed to initialize Utils: {ex.Message}", System.Drawing.Color.Red);
                return;
            }

            var memory = Memory.Instance;
            _modLoader.OnModLoaderInitialized += OnLoaderInitialized;

            var modDir = _modLoader.GetDirectoryForModId(_modConfig.ModId);


            Utils.SigScan("48 89 5C 24 ?? 48 89 74 24 ?? 57 48 83 EC 20 8B D9 41 8B F0 48 8D 0D ?? ?? ?? ?? 8B FA E8 ?? ?? ?? ?? 85 F6", "Field BGM", address =>
            {
                _FieldBGMHook = _hooks.CreateHook<FieldBGMDelegate>(FieldBGM, address).Activate();
            });

            Utils.SigScan("E9 ?? ?? ?? ?? 81 FB 91 01 00 00", "BGM Play", address =>
            {
                var funcAddress = Utils.GetGlobalAddress((nint)(address + 1));
                _BGMPlay = _hooks.CreateWrapper<PlayBGMDelegate>((long)funcAddress, out _);
            });

            Utils.SigScan("E9 ?? ?? ?? ?? 81 FB 91 01 00 00", "BGM Play2", address =>
            {
                var funcAddress = Utils.GetGlobalAddress((nint)(address + 1));
                _BGMPlay2 = _hooks.CreateWrapper<PlayBGM2Delegate>((long)funcAddress, out _);
            });

            // Gender check
            Utils.SigScan("48 83 EC 28 48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 0F B6 05 ?? ?? ?? ?? C1 E8 07", "isFemc", address =>
            {
                _IsFemc = _hooks.CreateWrapper<IsFemcDelegate>(address, out _);
            });

            // Time of day
            Utils.SigScan("E8 ?? ?? ?? ?? 84 C0 74 ?? E8 ?? ?? ?? ?? 3C 06", "TimeofDay", address =>
            {
                var funcAddress = Utils.GetGlobalAddress((nint)(address + 1));
                _TimeofDay = _hooks.CreateWrapper<TimeOFDayDelegate>((long)funcAddress, out _);
            });

            Utils.SigScan("E8 ?? ?? ?? ?? B8 0B 00 00 00 66 89 43 ?? E9 ?? ?? ?? ??", "Fix Map Screen BGM", address =>
            {
                memory.SafeWrite((nuint)address, new byte[] { 0x90, 0x90, 0x90, 0x90, 0x90 });
            });

            Utils.SigScan("40 53 48 83 EC 20 8B D9 48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 8B C3 99", "Bit Check", address =>
            {
                _BitCheck = _hooks.CreateWrapper<BitCheckDelegate>(address, out _);
                _logger.WriteLine($"Found BitCheck function at 0x{address:X}");
            });

            Utils.SigScan("E8 ?? ?? ?? ?? 0F BF C8 89 0D ?? ?? ?? ?? 89 74 24 ??", "GetTotalDay", address =>
            {
                var funcAddress = Utils.GetGlobalAddress((nint)(address + 1));
                _GetTotalDay = _hooks.CreateWrapper<GetTotalDayDelegate>((long)funcAddress, out _);
                _logger.WriteLine($"Found GetTotalDay at 0x{funcAddress:X}");
            });

            _modLoader.ModLoading += ModLoading;
        }

        private nuint FieldBGM(int fieldMajor, int fieldMinor, int tartarusFloor)
        {
            bool isFemc = _IsFemc();
            int timeOfDay = (int)_TimeofDay();
            int totalDays = _GetTotalDay();

            var (currentMonth, currentDay) = ConvertTotalDaysToDate(totalDays);

            Utils.LogDebug($"=== FieldBGM Analysis ===");
            Utils.LogDebug($"Field: {fieldMajor}_{fieldMinor}");
            Utils.LogDebug($"Tartarus Floor: {tartarusFloor}");
            Utils.LogDebug($"Time of Day: {timeOfDay}");
            Utils.LogDebug($"Is FeMC: {isFemc}");
            Utils.LogDebug($"Total Days (hex): 0x{totalDays:X}");
            Utils.LogDebug($"Current Date: {currentMonth}/{currentDay}");
            Utils.LogDebug($"Available BGM entries: {FinalBGMList.Count}");

            foreach (var bgm in FinalBGMList)
            {
                Utils.LogDebug($"--- Checking BGM {bgm.Adx} ---");

                if (!CheckBgmConditions(bgm, fieldMajor, fieldMinor, tartarusFloor, timeOfDay, isFemc, currentMonth, currentDay))
                    continue;

                Utils.LogDebug($"✓ MATCH FOUND! Playing custom BGM: {bgm}");
                _logger.WriteLine($"[Field BGM] Playing custom BGM {bgm.Adx} for field {fieldMajor}_{fieldMinor}", System.Drawing.Color.Green);

                var taskHandle = _BGMPlay2(bgm.Adx, 1);
                LogBgmSelection(bgm.Adx);
                return 1;
            }

            Utils.LogDebug($"✗ No custom BGM found, using original function");
            _logger.WriteLine($"[Field BGM] No custom BGM for field {fieldMajor}_{fieldMinor}, using default", System.Drawing.Color.Yellow);
            return _FieldBGMHook.OriginalFunction(fieldMajor, fieldMinor, tartarusFloor);
        }

        private bool CheckBgmConditions(BgmTable bgm, int fieldMajor, int fieldMinor, int tartarusFloor, int timeOfDay, bool isFemc, int currentMonth, int currentDay)
        {
            Utils.LogDebug($"  BGM Config: MajorId={bgm.MajorId}, MinorId={bgm.MinorId}, Floor={bgm.TartarusFloor}, Time={bgm.Time}");
            Utils.LogDebug($"  BGM Restrictions: FemcOnly={bgm.FemcOnly}, MaleOnly={bgm.MaleOnly}, Flag={bgm.Flag}");
            Utils.LogDebug($"  BGM Date Range: {bgm.StartMonth}/{bgm.StartDay} - {bgm.EndMonth}/{bgm.EndDay}");

            if (bgm.MajorId != -1 && bgm.MajorId != fieldMajor)
            {
                Utils.LogDebug($"  ✗ Major ID mismatch: need {bgm.MajorId}, got {fieldMajor}");
                return false;
            }
            Utils.LogDebug($"  ✓ Major ID check passed ({(bgm.MajorId == -1 ? "any" : bgm.MajorId.ToString())})");

            if (bgm.MinorId != -1 && bgm.MinorId != fieldMinor)
            {
                Utils.LogDebug($"  ✗ Minor ID mismatch: need {bgm.MinorId}, got {fieldMinor}");
                return false;
            }
            Utils.LogDebug($"  ✓ Minor ID check passed ({(bgm.MinorId == -1 ? "any" : bgm.MinorId.ToString())})");

            if (bgm.TartarusFloor != -1 && bgm.TartarusFloor != tartarusFloor)
            {
                Utils.LogDebug($"  ✗ Tartarus floor mismatch: need {bgm.TartarusFloor}, got {tartarusFloor}");
                return false;
            }
            Utils.LogDebug($"  ✓ Tartarus floor check passed ({(bgm.TartarusFloor == -1 ? "any" : bgm.TartarusFloor.ToString())})");

            if (bgm.Time != -1 && bgm.Time != timeOfDay)
            {
                Utils.LogDebug($"  ✗ Time mismatch: need {bgm.Time}, got {timeOfDay}");
                return false;
            }
            Utils.LogDebug($"  ✓ Time check passed ({(bgm.Time == -1 ? "any" : bgm.Time.ToString())})");

            if (bgm.FemcOnly && !isFemc)
            {
                Utils.LogDebug($"  ✗ FemcOnly restriction failed (player is male)");
                return false;
            }

            if (bgm.MaleOnly && isFemc)
            {
                Utils.LogDebug($"  ✗ MaleOnly restriction failed (player is female)");
                return false;
            }
            Utils.LogDebug($"  ✓ Gender restrictions passed");

            // Only check date range if date restrictions are specified
            if (bgm.StartMonth != -1 || bgm.StartDay != -1 || bgm.EndMonth != -1 || bgm.EndDay != -1)
            {
                if (!IsDateInRange(currentMonth, currentDay, bgm.StartMonth, bgm.StartDay, bgm.EndMonth, bgm.EndDay))
                {
                    Utils.LogDebug($"  ✗ Date {currentMonth}/{currentDay} not in range {bgm.StartMonth}/{bgm.StartDay} - {bgm.EndMonth}/{bgm.EndDay}");
                    return false;
                }
                Utils.LogDebug($"  ✓ Date range check passed");
            }
            else
            {
                Utils.LogDebug($"  ✓ No date restrictions (all -1)");
            }

            if (bgm.Flag != -1)
            {
                bool flagSet = BitCheck(bgm.Flag);
                if (!flagSet)
                {
                    Utils.LogDebug($"  ✗ Flag {bgm.Flag} not set");
                    return false;
                }
                Utils.LogDebug($"  ✓ Flag {bgm.Flag} is set");
            }
            else
            {
                Utils.LogDebug($"  ✓ No flag requirement");
            }

            Utils.LogDebug($"  ✓ ALL CONDITIONS PASSED!");
            return true;
        }
        private (int month, int day) ConvertTotalDaysToDate(int totalDays)
        {
            int[] daysInMonth = { 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };

            int currentMonth = 4;
            int currentDay = 1;
            int remainingDays = totalDays;

            Utils.LogDebug($"Converting totalDays: 0x{totalDays:X} ({totalDays} decimal)");

            if (totalDays == 0)
            {
                return (4, 1); // April 1st - game start
            }

            if (totalDays > 0)
            {
                while (remainingDays > 0)
                {
                    int daysLeftInMonth = daysInMonth[currentMonth - 1] - currentDay + 1;

                    if (remainingDays >= daysLeftInMonth)
                    {
                        remainingDays -= daysLeftInMonth;
                        currentMonth++;
                        if (currentMonth > 12)
                        {
                            currentMonth = 1;
                        }
                        currentDay = 1;
                    }
                    else
                    {
                        currentDay += remainingDays;
                        remainingDays = 0;
                    }
                }
            }
            else
            {
                currentMonth = 3;
                currentDay = 31;
                remainingDays = Math.Abs(totalDays);

                while (remainingDays > 0)
                {
                    if (remainingDays >= currentDay)
                    {
                        remainingDays -= currentDay;
                        currentMonth--;
                        if (currentMonth < 1)
                        {
                            currentMonth = 12;
                        }
                        currentDay = daysInMonth[currentMonth - 1];
                    }
                    else
                    {
                        currentDay -= remainingDays;
                        remainingDays = 0;
                    }
                }
            }

            Utils.LogDebug($"Converted to date: {currentMonth}/{currentDay}");
            return (currentMonth, currentDay);
        }
        private bool IsDateInRange(int currentMonth, int currentDay, int startMonth, int startDay, int endMonth, int endDay)
        {
            if (startMonth < endMonth || (startMonth == endMonth && startDay <= endDay))
            {
                if (currentMonth > startMonth && currentMonth < endMonth)
                    return true;
                if (currentMonth == startMonth && currentDay >= startDay)
                    return true;
                if (currentMonth == endMonth && currentDay <= endDay)
                    return true;
                return false;
            }
            else
            {
                if (currentMonth > startMonth || currentMonth < endMonth)
                    return true;
                if (currentMonth == startMonth && currentDay >= startDay)
                    return true;
                if (currentMonth == endMonth && currentDay <= endDay)
                    return true;
                return false;
            }
        }

        private bool BitCheck(int flagIndex)
        {
            try
            {
                return _BitCheck?.Invoke(flagIndex) ?? false;
            }
            catch (Exception ex)
            {
                Utils.LogDebug($"BitCheck failed for flag {flagIndex}: {ex.Message}");
                return false;
            }
        }

        private void LogBgmSelection(int bgmId)
        {
            _logger.WriteLine($"Field BGM Framework] Playing custom BGM with ID: {bgmId}", System.Drawing.Color.MediumSeaGreen);
        }

        private void OnLoaderInitialized()
        {
            _modLoader.OnModLoaderInitialized -= OnLoaderInitialized;
            _logger.WriteLine("[Field BGM Framework] Signature scanning completed");

            var ownModPath = Path.Combine(_modLoader.GetDirectoryForModId(_modConfig.ModId), "bgm");
            Utils.LogDebug($"[BGM DEBUG] Scanning own mod directory: {ownModPath}");
            if (Directory.Exists(ownModPath))
            {
                AddFolder(ownModPath);
            }
            else
            {
                _logger.WriteLine($"[BGM DEBUG] Own mod bgm directory doesn't exist: {ownModPath}", System.Drawing.Color.Red);
            }

            var loadedMods = _modLoader.GetActiveMods();
            foreach (var mod in loadedMods)
            {
                if (mod.Generic.ModId == _modConfig.ModId)
                    continue;

                var modBgmPath = Path.Combine(_modLoader.GetDirectoryForModId(mod.Generic.ModId), "bgm");
                if (Directory.Exists(modBgmPath))
                {
                    Utils.LogDebug($"Scanning existing mod: {mod.Generic.ModId}");
                    AddFolder(modBgmPath);
                }
            }

            var merged = MergeDistinctKeepingLast(allBgmLists);
            FinalBGMList = BgmTable.SortBgmTable(merged);

            _logger.WriteLine($"Final BGM Table loaded from mods ({FinalBGMList.Count} entries):", System.Drawing.Color.Cyan);
            foreach (var bgm in FinalBGMList)
            {
                _logger.WriteLine(bgm.ToString(), System.Drawing.Color.Gray);
            }

            _logger.WriteLine("Field BGM Framework initialization complete", System.Drawing.Color.Green);
        }

        private void ModLoading(IModV1 mod, IModConfigV1 modConfig)
        {
            Utils.LogDebug($"[BGM DEBUG] ModLoading event fired for: {modConfig.ModId}");

            var modsPath = Path.Combine(_modLoader.GetDirectoryForModId(modConfig.ModId), "bgm");
            Utils.LogDebug($"[BGM DEBUG] Checking path: {modsPath}");

            if (!Directory.Exists(modsPath))
            {
                Utils.LogDebug($"[BGM DEBUG] No bgm folder found for mod: {modConfig.ModId}");
                return;
            }

            Utils.LogDebug($"[BGM DEBUG] Found bgm folder for mod: {modConfig.ModId}");
            AddFolder(modsPath);
        }

        private void AddFolder(string folder)
        {
            Utils.LogDebug($"[BGM DEBUG] Checking folder: {folder}");

            if (!Directory.Exists(folder))
            {
                Utils.LogDebug($"[BGM DEBUG] Folder does not exist: {folder}");
                return;
            }

            var bgmTableJson = Path.Join(folder, "fieldbgm.json");
            Utils.LogDebug($"[BGM DEBUG] Looking for JSON file at: {bgmTableJson}");

            if (File.Exists(bgmTableJson))
            {
                Utils.LogDebug($"[BGM DEBUG] Found JSON file, loading from {bgmTableJson}");
                var loadedBgm = BgmTable.LoadFromJson(bgmTableJson);
                Utils.LogDebug($"[BGM DEBUG] Loaded {loadedBgm.Count} entries");
                allBgmLists.Add(loadedBgm);

                foreach (var bgm in loadedBgm)
                {
                    Utils.LogDebug($"[BGM DEBUG] Entry: {bgm}");
                }
            }
            else
            {
                Utils.LogDebug($"[BGM DEBUG] JSON file not found at: {bgmTableJson}");

                try
                {
                    var files = Directory.GetFiles(folder);
                    Utils.LogDebug($"[BGM DEBUG] Files in folder: {string.Join(", ", files)}");
                }
                catch (Exception ex)
                {
                    Utils.LogDebug($"[BGM DEBUG] Error listing files: {ex.Message}");
                }
            }
        }

        private List<BgmTable> MergeDistinctKeepingLast(List<List<BgmTable>> bgmLists)
        {
            var result = new List<BgmTable>();
            foreach (var list in bgmLists)
            {
                foreach (var bgm in list)
                {

                    result.RemoveAll(existing =>
                        existing.MajorId == bgm.MajorId &&
                        existing.MinorId == bgm.MinorId &&
                        existing.TartarusFloor == bgm.TartarusFloor &&
                        existing.Time == bgm.Time &&
                        existing.FemcOnly == bgm.FemcOnly &&
                        existing.MaleOnly == bgm.MaleOnly);

                    result.Add(bgm);
                }
            }
            return result;
        }

        public override void ConfigurationUpdated(Config configuration)
        {
            _configuration = configuration;
            _logger.WriteLine($"[{_modConfig.ModId}] Config Updated: Applying");
        }

        #region For Exports, Serialization etc.
#pragma warning disable CS8618
        public Mod() { }
#pragma warning restore CS8618
        #endregion
    }
}