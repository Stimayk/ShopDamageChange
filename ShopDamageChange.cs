using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using ShopAPI;

namespace ShopDamageChange
{
    public class ShopDamageChange : BasePlugin
    {
        public override string ModuleName => "[SHOP] Damage Multiplier";
        public override string ModuleDescription => "";
        public override string ModuleAuthor => "E!N";
        public override string ModuleVersion => "v1.0.0";

        private IShopApi? SHOP_API;
        private const string CategoryName = "DamageMultiplier";
        public static JObject? JsonDamageMultiplier { get; private set; }
        private readonly PlayerDamageMultiplier[] playerDamageMultipliers = new PlayerDamageMultiplier[65];

        public override void OnAllPluginsLoaded(bool hotReload)
        {
            SHOP_API = IShopApi.Capability.Get();
            if (SHOP_API == null) return;

            LoadConfig();
            InitializeShopItems();
            SetupTimersAndListeners();
        }

        private void LoadConfig()
        {
            string configPath = Path.Combine(ModuleDirectory, "../../configs/plugins/Shop/DamageMultiplier.json");
            if (File.Exists(configPath))
            {
                JsonDamageMultiplier = JObject.Parse(File.ReadAllText(configPath));
            }
        }

        private void InitializeShopItems()
        {
            if (JsonDamageMultiplier == null || SHOP_API == null) return;

            SHOP_API.CreateCategory(CategoryName, "Увеличение урона");

            foreach (var item in JsonDamageMultiplier.Properties().Where(p => p.Value is JObject))
            {
                Task.Run(async () =>
                {
                    int itemId = await SHOP_API.AddItem(
                        item.Name,
                        (string)item.Value["name"]!,
                        CategoryName,
                        (int)item.Value["price"]!,
                        (int)item.Value["sellprice"]!,
                        (int)item.Value["duration"]!
                    );
                    SHOP_API.SetItemCallbacks(itemId, OnClientBuyItem, OnClientSellItem, OnClientToggleItem);
                }).Wait();
            }
        }

        private void SetupTimersAndListeners()
        {
            RegisterListener<Listeners.OnClientDisconnect>(playerSlot => playerDamageMultipliers[playerSlot] = null!);
        }

        public void OnClientBuyItem(CCSPlayerController player, int itemId, string categoryName, string uniqueName,
            int buyPrice, int sellPrice, int duration, int count)
        {
            if (TryGetDamageMultiplierValue(uniqueName, out float damageMultiplierValue))
            {
                playerDamageMultipliers[player.Slot] = new PlayerDamageMultiplier(damageMultiplierValue, itemId);
            }
            else
            {
                Logger.LogError($"{uniqueName} has invalid or missing 'damagemultiplier' in config!");
            }
        }

        public void OnClientToggleItem(CCSPlayerController player, int itemId, string uniqueName, int state)
        {
            if (state == 1 && TryGetDamageMultiplierValue(uniqueName, out float damageMultiplierValue))
            {
                playerDamageMultipliers[player.Slot] = new PlayerDamageMultiplier(damageMultiplierValue, itemId);
            }
            else if (state == 0)
            {
                OnClientSellItem(player, itemId, uniqueName, 0);
            }
        }

        public void OnClientSellItem(CCSPlayerController player, int itemId, string uniqueName, int sellPrice)
        {
            playerDamageMultipliers[player.Slot] = null!;
        }

        public HookResult OnTakeDamage(DynamicHook hook)
        {
            CTakeDamageInfo damageInfo = hook.GetParam<CTakeDamageInfo>(1);

            CCSPlayerController? player = new CCSPlayerController(damageInfo.Attacker.Value?.Handle ?? 0);
            if (player == null || !player.IsValid || playerDamageMultipliers[player.Slot] == null)
                return HookResult.Continue;

            if (damageInfo.Attacker.Value is null)
                return HookResult.Continue;

            CCSWeaponBase? ccsWeaponBase = damageInfo.Ability.Value?.As<CCSWeaponBase>();

            if (ccsWeaponBase != null && ccsWeaponBase.IsValid)
            {
                CCSWeaponBaseVData? weaponData = ccsWeaponBase.VData;

                if (weaponData == null)
                    return HookResult.Continue;

                if (weaponData.GearSlot != gear_slot_t.GEAR_SLOT_RIFLE &&
                    weaponData.GearSlot != gear_slot_t.GEAR_SLOT_PISTOL)
                    return HookResult.Continue;

                float damageModifierValue = playerDamageMultipliers[player.Slot].DamageMultiplier;
                damageInfo.Damage *= damageModifierValue;
            }
            return HookResult.Continue;
        }

        private bool TryGetDamageMultiplierValue(string uniqueName, out float damageMultiplierValue)
        {
            damageMultiplierValue = 0f;
            if (JsonDamageMultiplier != null && JsonDamageMultiplier.TryGetValue(uniqueName, out var obj) &&
                obj is JObject jsonItem && jsonItem["damagemultiplier"] != null &&
                jsonItem["damagemultiplier"]!.Type != JTokenType.Null)
            {
                damageMultiplierValue = float.Parse(jsonItem["damagemultiplier"]!.ToString());
                return true;
            }

            return false;
        }

        public record class PlayerDamageMultiplier(float DamageMultiplier, int ItemID);
    }
}