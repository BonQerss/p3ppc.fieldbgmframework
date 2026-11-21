using System.Text.Json;
using p3ppc.fieldbgmframework.Configuration;
using p3ppc.fieldbgmframework.Template;
using Reloaded.Hooks.Definitions;
using Reloaded.Memory;
using Reloaded.Memory.Interfaces;
using Reloaded.Mod.Interfaces;

namespace p3ppc.fieldbgmframework
{
    public class Mod : ModBase
    {
        // Day 0 of the in-game calendar
        private static readonly DateTime FirstDay = new(2009, 4, 1);

        private readonly IModLoader _modLoader;
        private readonly IReloadedHooks _hooks;
        private readonly IModConfig _modConfig;
        private Config _configuration;

        private IHook<FieldBgmDelegate>? _fieldBgmHook;
        private PlayBgmDelegate? _playBgm;
        private IsFemcDelegate? _isFemc;
        private GetTimeOfDayDelegate? _getTimeOfDay;
        private BitCheckDelegate? _bitCheck;
        private GetTotalDayDelegate? _getTotalDay;

        private List<FieldBgm> _entries = new();

        public Mod(ModContext context)
        {
            _modLoader = context.ModLoader;
            _hooks = context.Hooks!;
            _configuration = context.Configuration;
            _modConfig = context.ModConfig;

            if (!Utils.Initialise(context.Logger, _configuration, _modLoader))
            {
                return;
            }

            _modLoader.OnModLoaderInitialized += LoadEntries;

            Utils.SigScan("48 89 5C 24 ?? 48 89 74 24 ?? 57 48 83 EC 20 8B D9 41 8B F0 48 8D 0D ?? ?? ?? ?? 8B FA E8 ?? ?? ?? ?? 85 F6", "FieldBgm", address =>
            {
                _fieldBgmHook = _hooks.CreateHook<FieldBgmDelegate>(FieldBgm, address).Activate();
            });

            Utils.SigScan("E9 ?? ?? ?? ?? 81 FB 91 01 00 00", "PlayBgm", address =>
            {
                _playBgm = _hooks.CreateWrapper<PlayBgmDelegate>((long)Utils.GetGlobalAddress(address + 1), out _);
            });

            Utils.SigScan("48 83 EC 28 48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 0F B6 05 ?? ?? ?? ?? C1 E8 07", "IsFemc", address =>
            {
                _isFemc = _hooks.CreateWrapper<IsFemcDelegate>(address, out _);
            });

            Utils.SigScan("E8 ?? ?? ?? ?? 84 C0 74 ?? E8 ?? ?? ?? ?? 3C 06", "GetTimeOfDay", address =>
            {
                _getTimeOfDay = _hooks.CreateWrapper<GetTimeOfDayDelegate>((long)Utils.GetGlobalAddress(address + 1), out _);
            });

            // The map screen restarts the field BGM when it closes, so that call is removed to keep a custom track playing
            Utils.SigScan("E8 ?? ?? ?? ?? B8 0B 00 00 00 66 89 43 ?? E9 ?? ?? ?? ??", "Map Screen BGM", address =>
            {
                Memory.Instance.SafeWrite((nuint)address, new byte[] { 0x90, 0x90, 0x90, 0x90, 0x90 });
            });

            Utils.SigScan("40 53 48 83 EC 20 8B D9 48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 8B C3 99", "BitCheck", address =>
            {
                _bitCheck = _hooks.CreateWrapper<BitCheckDelegate>(address, out _);
            });

            Utils.SigScan("E8 ?? ?? ?? ?? 0F BF C8 89 0D ?? ?? ?? ?? 89 74 24 ??", "GetTotalDay", address =>
            {
                _getTotalDay = _hooks.CreateWrapper<GetTotalDayDelegate>((long)Utils.GetGlobalAddress(address + 1), out _);
            });
        }

        private nuint FieldBgm(int major, int minor, int floor)
        {
            if (_playBgm != null && _isFemc != null && _getTimeOfDay != null && _getTotalDay != null)
            {
                bool femc = _isFemc();
                int time = (int)_getTimeOfDay();
                DateTime date = FirstDay.AddDays(_getTotalDay());
                FieldBgm? bgm = _entries.FirstOrDefault(e => e.Matches(major, minor, floor, time, femc, date) && (e.Flag == -1 || (_bitCheck != null && _bitCheck(e.Flag))));
                if (bgm != null)
                {
                    Utils.LogDebug($"Playing BGM {bgm.Adx} for field {major}_{minor}.");
                    _playBgm(bgm.Adx, 1);
                    return 1;
                }
            }

            return _fieldBgmHook!.OriginalFunction(major, minor, floor);
        }

        // Every mod's bgm/fieldbgm.json, in load order, with later entries replacing earlier ones for the same place and time
        private void LoadEntries()
        {
            _modLoader.OnModLoaderInitialized -= LoadEntries;

            var mods = _modLoader.GetActiveMods().Select(m => m.Generic.ModId).Where(id => id != _modConfig.ModId).Prepend(_modConfig.ModId);
            foreach (string id in mods)
            {
                string path = Path.Combine(_modLoader.GetDirectoryForModId(id), "bgm", "fieldbgm.json");
                if (!File.Exists(path))
                {
                    continue;
                }

                try
                {
                    foreach (FieldBgm bgm in JsonSerializer.Deserialize<List<FieldBgm>>(File.ReadAllText(path)) ?? new())
                    {
                        _entries.RemoveAll(e => e.SamePlaceAndTime(bgm));
                        _entries.Add(bgm);
                    }
                }
                catch (Exception e)
                {
                    Utils.LogError($"Couldn't read {path}", e);
                }
            }

            Utils.Log($"Loaded {_entries.Count} field BGM entries.");
            foreach (FieldBgm bgm in _entries)
            {
                Utils.LogDebug(bgm.ToString());
            }
        }

        public override void ConfigurationUpdated(Config configuration)
        {
            _configuration = configuration;
            Utils.UpdateConfig(configuration);
        }

        private delegate nuint FieldBgmDelegate(int major, int minor, int floor);

        private delegate nuint PlayBgmDelegate(int bgmId, int param2);

        private delegate nuint GetTimeOfDayDelegate();

        private delegate bool IsFemcDelegate();

        private delegate bool BitCheckDelegate(int flag);

        private delegate int GetTotalDayDelegate();

#pragma warning disable CS8618
        public Mod() { }
#pragma warning restore CS8618
    }
}
