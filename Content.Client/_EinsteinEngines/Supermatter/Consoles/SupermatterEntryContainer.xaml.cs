using Content.Client.Atmos.EntitySystems;
using Content.Client.Stylesheets;
using Content.Shared._EinsteinEngines.Supermatter.Components;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Prototypes;
using Robust.Client.AutoGenerated;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.XAML;
using System.Linq;

namespace Content.Client._EinsteinEngines.Supermatter.Consoles;

[GenerateTypedNameReferences]
public sealed partial class SupermatterEntryContainer : BoxContainer
{
    public NetEntity NetEntity;

    private readonly IEntityManager _entManager;
    private readonly IResourceCache _cache;

    public SupermatterEntryContainer(NetEntity uid)
    {
        RobustXamlLoader.Load(this);

        _entManager = IoCManager.Resolve<IEntityManager>();
        _cache = IoCManager.Resolve<IResourceCache>();

        NetEntity = uid;

        // Load fonts
        var headerFont = new VectorFont(_cache.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Bold.ttf"), 11);
        var normalFont = new VectorFont(_cache.GetResource<FontResource>("/Fonts/NotoSansDisplay/NotoSansDisplay-Regular.ttf"), 11);
        var monoFont = new VectorFont(_cache.GetResource<FontResource>("/EngineFonts/NotoSans/NotoSansMono-Regular.ttf"), 10);

        // Set fonts
        SupermatterNameLabel.FontOverride = headerFont;

        SupermatterStatusLabel.FontOverride = normalFont;

        IntegrityLabel.FontOverride = normalFont;
        PowerLabel.FontOverride = normalFont;
        RadiationLabel.FontOverride = normalFont;
        MolesLabel.FontOverride = normalFont;
        TemperatureLabel.FontOverride = normalFont;
        TemperatureLimitLabel.FontOverride = normalFont;
        WasteLabel.FontOverride = normalFont;
        AbsorptionLabel.FontOverride = normalFont;

        IntegrityBarLabel.FontOverride = monoFont;
        PowerBarLabel.FontOverride = monoFont;
        RadiationBarLabel.FontOverride = monoFont;
        MolesBarLabel.FontOverride = monoFont;
        TemperatureBarLabel.FontOverride = monoFont;
        TemperatureLimitBarLabel.FontOverride = monoFont;
        WasteBarLabel.FontOverride = monoFont;
        AbsorptionBarLabel.FontOverride = monoFont;
    }

    private Dictionary<string, (Label label, ProgressBar bar, PanelContainer border, float leftSize, float rightSize, Color leftColor, Color middleColor, Color rightColor)>? _engineDictionary;

    public void UpdateEntry(SupermatterConsoleEntry entry, bool isFocus, SupermatterFocusData? focusData = null)
    {
        NetEntity = entry.NetEntity;

        // Update supermatter name
        SupermatterNameLabel.Text = Loc.GetString("supermatter-console-window-label-sm", ("name", entry.EntityName));

        // Update supermatter status
        var statusText = entry.EntityStatus.ToString().ToLower();
        SupermatterStatusLabel.Text = Loc.GetString($"supermatter-console-window-status-{statusText}");

        SupermatterStatusLabel.FontColorOverride = entry.EntityStatus switch
        {
            >= SupermatterStatusType.Danger => StyleNano.DangerousRedFore,
            >= SupermatterStatusType.Caution => StyleNano.ConcerningOrangeFore,
            SupermatterStatusType.Normal => StyleNano.GoodGreenFore,
            SupermatterStatusType.Inactive => Color.DarkGray,
            _ => StyleNano.DisabledFore
        };

        // Focus updates
        FocusContainer.Visible = isFocus;

        if (isFocus)
        {
            if (focusData != null)
            {
                var red = StyleNano.DangerousRedFore;
                var orange = StyleNano.ConcerningOrangeFore;
                var green = StyleNano.GoodGreenFore;
                var turqoise = Color.FromHex("#00fff7");

                // Set the engine dictionary once
                _engineDictionary ??= new()
                {
                    { "integrity",   ( IntegrityBarLabel,   IntegrityBar,   IntegrityBarBorder,   0.9f, 0.1f, red,      orange, green ) },
                    { "power",       ( PowerBarLabel,       PowerBar,       PowerBarBorder,       0.9f, 0.1f, green,    orange, red   ) },
                    { "radiation",   ( RadiationBarLabel,   RadiationBar,   RadiationBarBorder,   0.1f, 0.9f, green,    orange, red   ) },
                    { "moles",       ( MolesBarLabel,       MolesBar,       MolesBarBorder,       0.5f, 0.5f, green,    orange, red   ) },
                    { "temperature", ( TemperatureBarLabel, TemperatureBar, TemperatureBarBorder, 0.9f, 0.1f, turqoise, green,  red   ) },
                    { "waste",       ( WasteBarLabel,       WasteBar,       WasteBarBorder,       0.5f, 0.5f, green,    orange, red   ) }
                };

                // Update the bar values every time
                Dictionary<string, float> barData = new()
                {
                    { "integrity",   focusData.Value.Integrity },
                    { "power",       focusData.Value.Power },
                    { "radiation",   focusData.Value.Radiation },
                    { "moles",       focusData.Value.GasStorage.TotalMoles },
                    { "temperature", focusData.Value.GasStorage.Temperature },
                    { "waste",       focusData.Value.HeatModifier }
                };

                // Special cases
                var powerValue = barData["power"];
                var powerPrefix = powerValue switch { >= 1000 => "G", >= 1 => "M", _ => "" };
                var powerMultiplier = powerValue switch { >= 1000 => 0.001, >= 1 => 1, _ => 1000 };
                _engineDictionary["power"].label.Text = Loc.GetString(
                    "supermatter-console-window-label-power-bar",
                    ("power", (powerValue * powerMultiplier).ToString("0.000")),
                    ("prefix", powerPrefix));

                var temperatureLimit = focusData.Value.TemperatureLimit;
                TemperatureBar.MaxValue = temperatureLimit;
                TemperatureLimitBarLabel.Text = Loc.GetString("supermatter-console-window-label-temperature-bar", ("temperature", temperatureLimit.ToString("0.00")));

                var absorptionRatio = focusData.Value.AbsorptionRatio;
                AbsorptionBarLabel.Text = Loc.GetString("supermatter-console-window-label-absorption-bar", ("absorption", absorptionRatio.ToString("0")));

                // Update engine bars
                foreach (var bar in _engineDictionary)
                {
                    var current = bar.Value;
                    var value = barData[bar.Key];
                    UpdateEngineBar(current.bar, current.border, value, current.leftSize, current.rightSize, current.leftColor, current.middleColor, current.rightColor);

                    if (bar.Key == "power")
                        continue;

                    current.label.Text = Loc.GetString($"supermatter-console-window-label-{bar.Key}-bar", (bar.Key, value.ToString("0.00")));
                }

                // Update gas bars
                var atmosphereSystem = _entManager.System<AtmosphereSystem>();
                var gases = atmosphereSystem.Gases.OrderByDescending(gas => GetStoredGas(gas, focusData));
                var index = 0;

                foreach (var gas in gases)
                {
                    var name = gas.Name;
                    var color = Color.FromHex("#" + gas.Color);
                    var value = GetStoredGas(gas, focusData) / focusData.Value.GasStorage.TotalMoles * 100;

                    UpdateGasBar(index, GasTable, name, color, value);
                    index++;
                }
            }
        }
    }

    private static float GetStoredGas(GasPrototype gas, SupermatterFocusData? focusData)
    {
        var id = int.Parse(gas.ID);

        if (focusData == null)
            return 0f;

        return focusData.Value.GasStorage.GetMoles((Gas)id);
    }

    private static void UpdateEngineBar(ProgressBar bar, PanelContainer border, float value, float leftSize, float rightSize, Color leftColor, Color middleColor, Color rightColor)
    {
        var clamped = Math.Clamp(value, bar.MinValue, bar.MaxValue);

        var normalized = clamped / bar.MaxValue;
        var leftHsv = Color.ToHsv(leftColor);
        var middleHsv = Color.ToHsv(middleColor);
        var rightHsv = Color.ToHsv(rightColor);

        // Ensure leftSize and rightSize add up to 1.0 or the transition won't be smooth
        var minColor = new Vector4(0, 0, 0, 0);
        var maxColor = new Vector4(1, 1, 1, 1);
        Color finalColor;

        if (normalized <= leftSize)
        {
            normalized /= leftSize; // Adjust range to 0.0 to 1.0
            var calcColor = Vector4.Lerp(leftHsv, middleHsv, normalized);
            var clampedColor = Vector4.Clamp(calcColor, minColor, maxColor);
            finalColor = Color.FromHsv(clampedColor);
        }

        else
        {
            normalized = (normalized - leftSize) / rightSize; // Adjust range to 0.0 to 1.0
            var calcColor = Vector4.Lerp(middleHsv, rightHsv, normalized);
            var clampedColor = Vector4.Clamp(calcColor, minColor, maxColor);
            finalColor = Color.FromHsv(clampedColor);
        }

        // Check if null first to avoid repeatedly creating this.
        bar.ForegroundStyleBoxOverride ??= new StyleBoxFlat();
        border.PanelOverride ??= new StyleBoxFlat();

        var barOverride = (StyleBoxFlat)bar.ForegroundStyleBoxOverride;
        barOverride.BackgroundColor = finalColor;

        var panelOverride = (StyleBoxFlat)border.PanelOverride;
        panelOverride.BackgroundColor = finalColor;

        bar.Value = clamped;
    }

    private static void UpdateGasBar(int index, Control table, string name, Color color, float value)
    {
        // Make new UI entry if required
        if (index >= table.ChildCount)
        {
            var newEntryContainer = new SupermatterGasBarContainer();

            // Add the entry to the current table
            table.AddChild(newEntryContainer);
        }

        // Update values and UI elements
        var tableChild = table.GetChild(index);

        if (tableChild is not SupermatterGasBarContainer)
        {
            table.RemoveChild(tableChild);
            UpdateGasBar(index, table, name, color, value);

            return;
        }

        var entryContainer = (SupermatterGasBarContainer)tableChild;

        entryContainer.UpdateEntry(name, color, value);
    }
}
