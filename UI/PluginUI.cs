using Dalamud.Bindings.ImGui;
using Dalamud.Game.Inventory;
using Lumina.Excel.Sheets;
using System.Diagnostics;
using System.Numerics;

namespace Beastmaster;

public sealed class PluginUI
{
    private static readonly (string Key, string Label)[] MainSections =
    [
        ("quests", "Beastmaster Quest Chain"),
        ("catalog", "Beast Catalog"),
        ("party", "Beast Arena Trials"),
        ("equipment", "Recommended Gear"),
        ("combinations", "Recommended Team Comps"),
        ("sequences", "Skill Sequence"),
        ("rules", "Rule Mode"),
        ("commands", "Quick Commands"),
        ("auto-output", "Auto Rotation"),
        ("settings", "Settings"),
        ("debug", "DEBUG"),
        ("arena-navigation", "Go to the Beast Arena"),
    ];

    private static readonly (uint ActionId, string Name)[] SequenceActions =
    [
        (44879, "Shattering Slash"),
        (44883, "Shattering Bite Axe"),
        (44885, "Shieldbreaker Cleave"),
        (44893, "Shield Bash"),
        (44890, "Release"),
        (44891, "Finishing Blow"),
        (44881, "Beast Whistle 1"),
        (44892, "Beast Whistle 2"),
        (44894, "Beast Whistle 3"),
        (44895, "Borrow"),
        (44896, "Beastskin"),
        (44897, "Bugskin"),
        (44898, "Winged Swoop"),
        (44899, "Sow Seed"),
        (44900, "Aquatic Wave"),
        (44901, "Scaleskin"),
        (44902, "Soulshatter Charm"),
        (44903, "Purify Corpse"),
        (44904, "Cheer"),
        (44905, "Rally"),
    ];

    private readonly BeastmasterConfiguration configuration;
    private readonly BeastmasterProgressService progressService;
    private readonly BeastmasterQuestService questService;
    private readonly BeastmasterNavigationService navigationService;
    private readonly BeastmasterDebugDataService debugDataService;
    private readonly BeastmasterAutoCaptureService autoCaptureService;
    private readonly BeastmasterCatalogSyncService catalogSyncService;
    private readonly BeastmasterAchievementSyncService achievementSyncService;
    private readonly BeastmasterNotebookSyncService notebookSyncService;
    private readonly BeastmasterSequenceService sequenceService;
    private readonly BeastmasterRuleService ruleService;
    private readonly BeastmasterPetPartyService petPartyService;
    private string debugQuery = "Tame";
    private string debugActionId = "44890";
    private string debugResult = "Click the button to load client data.";
    private bool debugUseAdjustedActionId = true;
    private int debugSearchType;
    private int debugProjectDataType;
    private int debugCurrentStateType;
    private DateTime nextQuestStatusRefreshUtc = DateTime.MinValue;
    private DateTime nextGaugeRefreshUtc = DateTime.MinValue;
    private BeastmasterGaugeSnapshot gaugeSnapshot = BeastmasterGaugeSnapshot.Unavailable("Waiting to load");
    private int autoOutputCollapseState;
    private int selectedEquipmentSet;
    private int selectedBattleLogIndex;
    private DateTime nextEquipmentRefreshUtc = DateTime.MinValue;
    private readonly Dictionary<string, (uint ItemId, uint EquippedCount, uint InventoryCount, uint ArmoryCount)> equipmentOwnership = new(StringComparer.Ordinal);
    private bool isMainWindowOpen;
    private int newRuleTerritoryId;
    private string ruleImportStatus = string.Empty;
    private string partyPresetStatus = string.Empty;
    private bool arenaTabSelectionInitialized;
    private bool wasInAchievementsTab;

    public PluginUI(
        BeastmasterConfiguration configuration,
        BeastmasterProgressService progressService,
        BeastmasterQuestService questService,
        BeastmasterNavigationService navigationService,
        BeastmasterDebugDataService debugDataService,
        BeastmasterAutoCaptureService autoCaptureService,
        BeastmasterCatalogSyncService catalogSyncService,
        BeastmasterAchievementSyncService achievementSyncService,
        BeastmasterNotebookSyncService notebookSyncService,
        BeastmasterSequenceService sequenceService,
        BeastmasterRuleService ruleService,
        BeastmasterPetPartyService petPartyService)
    {
        this.configuration = configuration;
        this.progressService = progressService;
        this.questService = questService;
        this.navigationService = navigationService;
        this.debugDataService = debugDataService;
        this.autoCaptureService = autoCaptureService;
        this.catalogSyncService = catalogSyncService;
        this.achievementSyncService = achievementSyncService;
        this.notebookSyncService = notebookSyncService;
        this.sequenceService = sequenceService;
        this.ruleService = ruleService;
        this.petPartyService = petPartyService;
    }

    public void OpenMainWindow()
    {
        isMainWindowOpen = true;
    }

    public void Draw()
    {
        RefreshGaugeSnapshot();
        DrawAutoCaptureOverlay();
        DrawPetPartyOverlay();
        DriveUseActionScan();
        if (!isMainWindowOpen)
        {
            return;
        }

        ImGui.SetNextWindowSize(new Vector2(900f, 600f), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(700f, 460f), new Vector2(float.MaxValue, float.MaxValue));
        if (!ImGui.Begin($"Beastmaster Assistant v{GetType().Assembly.GetName().Version}", ref isMainWindowOpen))
        {
            ImGui.End();
            return;
        }

        DrawMainShell();
        ImGui.End();
    }

    private void DrawAutoCaptureOverlay()
    {
        const uint beastmasterClassJobId = 43;
        if (!autoCaptureService.IsEnabled
            || !DalamudApi.ClientState.IsLoggedIn
            || DalamudApi.ObjectTable.LocalPlayer == null
            || DalamudApi.PlayerState.ClassJob.RowId != beastmasterClassJobId)
        {
            return;
        }

        ImGui.SetNextWindowPos(new Vector2(20f, 180f), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowBgAlpha(0.9f);
        if (!ImGui.Begin(
                "##BeastmasterAutoCaptureOverlay",
                ImGuiWindowFlags.NoTitleBar
                | ImGuiWindowFlags.AlwaysAutoResize
                | ImGuiWindowFlags.NoScrollbar
                | ImGuiWindowFlags.NoScrollWithMouse
                | ImGuiWindowFlags.NoFocusOnAppearing
                | ImGuiWindowFlags.NoNav))
        {
            ImGui.End();
            return;
        }

        DrawAutoOutputHeader();
        if (ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows)
            && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            configuration.SelectedMainSection = "settings";
            configuration.Save();
            isMainWindowOpen = true;
        }

        if (autoOutputCollapseState == 2)
        {
            ImGui.End();
            return;
        }

        ImGui.Separator();
        ImGui.TextColored(new Vector4(0.35f, 0.85f, 0.55f, 1f), autoCaptureService.StatusText);
        if (configuration.ShowGaugeInOverlay)
        {
            ImGui.Text($"Next skill: {autoCaptureService.NextActionName}");
            if (!string.IsNullOrWhiteSpace(autoCaptureService.NextActionReason))
            {
                ImGui.TextDisabled($"Reason: {autoCaptureService.NextActionReason}");
            }
        }
        if (configuration.ShowGaugeInOverlay)
        {
            DrawOverlayGaugeSummary();
        }
        if (configuration.ShowGaugeInOverlay)
        {
            DrawOverlayTargetStatus();
        }
        if (configuration.ShowGaugeInOverlay)
        {
            DrawOverlayAdvancedCandidates();
        }
        ImGui.Separator();
        var tryCapture = autoCaptureService.TryCapture;
        if (ImGui.Checkbox("Capture Threshold", ref tryCapture))
        {
            autoCaptureService.SetTryCapture(tryCapture);
        }
        ImGui.SameLine();
        DrawCompactCaptureHpThreshold();
        if (configuration.ShowGaugeInOverlay)
        {
            var basicComboEnabled = autoCaptureService.BasicComboEnabled;
            if (ImGui.Checkbox("Basic Combo (1→2→3)", ref basicComboEnabled))
            {
                autoCaptureService.SetBasicComboEnabled(basicComboEnabled);
            }
        }
        if (autoOutputCollapseState == 0)
        {
            DrawAdvancedActionToggles(compactFinalStrike: true);

            var sequenceEnabled = sequenceService.Enabled;
            if (ImGui.Checkbox("##overlay-sequence-enabled", ref sequenceEnabled))
            {
                sequenceService.SetEnabled(sequenceEnabled);
            }
            ImGui.SameLine();
            DrawSequenceSelector(string.Empty, "##overlay-sequence-selector");
            ImGui.TextDisabled($"Status: {sequenceService.Status}");
            if (sequenceService.IsControlling && ImGui.Button("Abort Skill Sequence"))
            {
                sequenceService.Abort("Manually aborted, waiting for the next party countdown");
            }
        }

        if (ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows)
            && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            configuration.SelectedMainSection = "settings";
            configuration.Save();
            isMainWindowOpen = true;
        }

        ImGui.End();
    }

    private void DrawPetPartyOverlay()
    {
        var snapshot = petPartyService.Snapshot;
        if (!snapshot.Available)
        {
            return;
        }

        var presets = configuration.PartyPresets;
        if (presets.Count == 0)
        {
            return;
        }

        var selectedIndex = Math.Clamp(configuration.SelectedPartyPresetIndex, 0, presets.Count - 1);
        var selected = presets[selectedIndex];
        ImGui.SetNextWindowPos(new Vector2(280f, 180f), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowBgAlpha(0.92f);
        if (!ImGui.Begin("##BeastmasterPartyOverlay",
                ImGuiWindowFlags.NoTitleBar
                | ImGuiWindowFlags.AlwaysAutoResize
                | ImGuiWindowFlags.NoScrollbar
                | ImGuiWindowFlags.NoScrollWithMouse
                | ImGuiWindowFlags.NoFocusOnAppearing
                | ImGuiWindowFlags.NoNav))
        {
            ImGui.End();
            return;
        }

        ImGui.Text("Trial Formation");
        ImGui.TextDisabled($"Current Formation · {snapshot.MemberCount}/{snapshot.Capacity}");
        var presetNames = string.Join('\0', presets.Select(preset => preset.Name)) + '\0';
        ImGui.SetNextItemWidth(180f);
        if (ImGui.Combo("##party-overlay-preset", ref selectedIndex, presetNames))
        {
            configuration.SelectedPartyPresetIndex = selectedIndex;
            configuration.Save();
            selected = presets[selectedIndex];
        }
        ImGui.SameLine();
        ImGui.BeginDisabled(petPartyService.IsApplying || !CanApplyPartyPreset(selected));
        PushPartyApplyButtonStyle();
        if (ImGui.Button(petPartyService.IsApplying ? "Applying..." : "Apply"))
        {
            petPartyService.TryApply(selected);
        }
        ImGui.PopStyleColor(3);
        ImGui.EndDisabled();
        if (!string.IsNullOrWhiteSpace(petPartyService.ApplyStatus))
        {
            ImGui.TextWrapped(petPartyService.ApplyStatus);
        }

        ImGui.End();
    }

    private static void PushPartyApplyButtonStyle()
    {
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.67f, 0.52f, 0.27f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.78f, 0.63f, 0.36f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.58f, 0.43f, 0.21f, 1f));
    }

    private void DrawAutoOutputHeader()
    {
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.85f, 0.35f, 1f));
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Beastmaster ACR");
        ImGui.PopStyleColor();

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(autoOutputCollapseState switch
            {
                0 => "Left-click to half-collapse: hides advanced skill buttons and the skill sequence",
                1 => "Left-click to fully collapse the overlay",
                _ => "Left-click to expand the overlay",
            });
        }

        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            autoOutputCollapseState = (autoOutputCollapseState + 1) % 3;
        }

        ImGui.SameLine();
        var headerStatus = !autoCaptureService.IsEnabled
            ? "Off"
            : autoCaptureService.IsPaused ? "Pause" : "Auto";
        if (DrawOverlayStatusBadge(
            headerStatus,
            autoCaptureService.IsEnabled && !autoCaptureService.IsPaused
                ? new Vector4(0.2f, 0.42f, 0.28f, 1f)
                : new Vector4(0.3f, 0.3f, 0.34f, 1f),
            autoCaptureService.IsEnabled && !autoCaptureService.IsPaused
                ? new Vector4(0.45f, 1f, 0.58f, 1f)
                : new Vector4(0.7f, 0.7f, 0.75f, 1f)))
        {
            if (autoCaptureService.IsEnabled)
            {
                autoCaptureService.SetPaused(!autoCaptureService.IsPaused);
            }
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(!autoCaptureService.IsEnabled
                ? "Please enable Auto Rotation in the Auto Rotation tab first"
                : autoCaptureService.IsPaused ? "Click to resume Auto Rotation" : "Click to pause Auto Rotation");
        }

        ImGui.SameLine();
        var tryCapture = autoCaptureService.TryCapture;
        var forceCapture = autoCaptureService.ForceCapture;
        if (DrawOverlayStatusBadge(
            "Capture",
            forceCapture
                ? new Vector4(0.48f, 0.12f, 0.12f, 1f)
                : tryCapture
                    ? new Vector4(0.2f, 0.42f, 0.28f, 1f)
                    : new Vector4(0.3f, 0.3f, 0.34f, 1f),
            forceCapture
                ? new Vector4(1f, 0.42f, 0.42f, 1f)
                : tryCapture
                    ? new Vector4(0.45f, 1f, 0.58f, 1f)
                    : new Vector4(0.7f, 0.7f, 0.75f, 1f)))
        {
            if (forceCapture)
            {
                autoCaptureService.SetTryCapture(false);
            }
            else if (tryCapture)
            {
                autoCaptureService.SetForceCapture(true);
            }
            else
            {
                autoCaptureService.SetTryCapture(true);
            }
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(forceCapture
                ? "Force Capture: ignores the target's capture buff but still respects the HP threshold; click to turn capture off"
                : tryCapture
                    ? "Try Capture: judged by HP and the capture buff; click to switch to Force Capture"
                    : "Capture is off; click to enable Try Capture");
        }

        ImGui.SameLine();
        var activeAttack = autoCaptureService.ActiveAttack;
        if (DrawOverlayStatusBadge(
            "Active",
            activeAttack
                ? new Vector4(0.2f, 0.42f, 0.28f, 1f)
                : new Vector4(0.3f, 0.3f, 0.34f, 1f),
            activeAttack
                ? new Vector4(0.45f, 1f, 0.58f, 1f)
                : new Vector4(0.7f, 0.7f, 0.75f, 1f)))
        {
            autoCaptureService.SetActiveAttack(!activeAttack);
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(activeAttack
                ? "Allows normal ACR to actively attack, capture, and use Beast Arena skills while not in combat"
                : "Normal ACR will not attack, capture, or use Beast Arena skills while not in combat");
        }
    }

    private static bool DrawOverlayStatusBadge(string label, Vector4 background, Vector4 textColor)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 8f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(7f, 2f));
        ImGui.PushStyleColor(ImGuiCol.Button, background);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, background);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, background);
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleColor(ImGuiCol.Text, textColor);
        var clicked = ImGui.Button(label);
        ImGui.PopStyleColor(5);
        ImGui.PopStyleVar(2);
        return clicked;
    }

    private void DrawMainShell()
    {
        if (!ImGui.BeginTable(
                "BeastmasterMainShell",
                2,
                ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingStretchProp))
        {
            return;
        }

        ImGui.TableSetupColumn("Navigate", ImGuiTableColumnFlags.WidthFixed, 190f);
        ImGui.TableSetupColumn("Content", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        DrawSidebar();
        ImGui.TableNextColumn();
        DrawContent();
        ImGui.EndTable();
    }

    private void RefreshGaugeSnapshot()
    {
        if (DateTime.UtcNow < nextGaugeRefreshUtc)
        {
            return;
        }

        gaugeSnapshot = BeastmasterGaugeSnapshot.Read();
        nextGaugeRefreshUtc = DateTime.UtcNow.AddMilliseconds(100);
    }

    private void DrawOverlayGaugeSummary()
    {
        if (!gaugeSnapshot.Available)
        {
            return;
        }

        var entry = gaugeSnapshot.SummonEntry;
        ImGui.Text(entry == null
            ? $"Current Beast: {(gaugeSnapshot.SummonDataId == 0 ? "Not Summoned" : gaugeSnapshot.SummonName)}"
            : $"Current Beast: {entry.Name} [{entry.Attribute}]");
        DrawOverlayGaugeBar("Skill Power", gaugeSnapshot.Tp, BeastmasterGaugeSnapshot.MaximumGauge, new Vector4(0.95f, 0.75f, 0.2f, 1f));
        DrawOverlayGaugeBar("Beast Power", gaugeSnapshot.BeastPower, BeastmasterGaugeSnapshot.MaximumGauge, new Vector4(0.35f, 0.7f, 1f, 1f));
        ImGui.Text($"Heart of Taming: {gaugeSnapshot.BeastHeartStacks} stacks | Heart of the Beast Spirit: {gaugeSnapshot.BeastSoulStacks} stacks");
    }

    private void DrawOverlayTargetStatus()
    {
        ImGui.Text("Current Target");
        var target = DalamudApi.TargetManager.Target;
        if (target is not Dalamud.Game.ClientState.Objects.Types.IBattleChara)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("No valid target");
            return;
        }

        ImGui.SameLine();
        ImGui.Text(target.Name.TextValue);
        ImGui.SameLine();
        ImGui.TextDisabled($"{autoCaptureService.TargetHpPercent:0.#}% · {autoCaptureService.TargetStatus}");
        if (configuration.ShowGaugeInOverlay)
        {
            ImGui.TextDisabled($"Capture: {autoCaptureService.CaptureState}");
        }
    }

    private static void DrawOverlayGaugeBar(string label, float value, float maximum, Vector4 color)
    {
        var fraction = Math.Clamp(value / maximum, 0f, 1f);
        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, color);
        ImGui.ProgressBar(fraction, new Vector2(-1f, 14f), $"{label} {value:0}/{maximum:0}");
        ImGui.PopStyleColor();
    }

    private void DrawCompactCaptureHpThreshold()
    {
        var threshold = Math.Clamp(configuration.CaptureHpThreshold, 1f, 100f);
        ImGui.SetNextItemWidth(70f);
        if (ImGui.InputFloat("##overlay-capture-threshold", ref threshold, 0f, 0f, "%.0f%%"))
        {
            threshold = Math.Clamp(threshold, 1f, 100f);
            configuration.CaptureHpThreshold = threshold;
            configuration.Save();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Capture HP Threshold: uses Capture when the target's HP is at or below this percentage");
        }
    }

    private void DrawOverlayAdvancedCandidates()
    {
        if (!ImGui.CollapsingHeader("Advanced Skill Candidates##BeastmasterAdvancedCandidates"))
        {
            return;
        }

        var entry = gaugeSnapshot.SummonEntry;
        if (entry == null)
        {
            ImGui.TextDisabled("Current beast not recognized; cannot generate advanced skill candidates.");
            return;
        }

        ImGui.TextColored(GetAttributeColor(entry.Attribute), $"Attribute: {entry.Attribute}");
        DrawAdvancedCandidate(
            configuration.BeastHeartCooperationEnabled ? "Taming Link (Yellow Orb)" : "Beast Spirit Link (Blue Orb)",
            configuration.BeastHeartCooperationEnabled
                ? $"{GetActionName(47093)} → Attribute Axe"
                : $"Attribute Axe → {GetActionName(47093)}",
            configuration.BeastHeartCooperationEnabled || configuration.BeastSoulCooperationEnabled,
            gaugeSnapshot.Tp >= 100 && gaugeSnapshot.BeastPower >= 100
                ? "Skill Power and Beast Power meet the basic threshold"
                : $"Insufficient resources: Skill Power {gaugeSnapshot.Tp}/100, Beast Power {gaugeSnapshot.BeastPower}/100");
        var thirdFormEnabled = configuration.PhysicalThirdFormEnabled || configuration.MagicalThirdFormEnabled;
        DrawAdvancedCandidate(
            configuration.PhysicalThirdFormEnabled ? "Convergence (Physical)" : "Convergence (Magic)",
            GetThirdFormActionName(gaugeSnapshot),
            thirdFormEnabled,
            GetThirdFormReason(gaugeSnapshot));
        ImGui.TextDisabled($"Release: adjusted at runtime ({(configuration.AutoReleaseEnabled ? "On" : "Off")})");
        if (ImGui.Button($"Manually Use Ultimate##manual-ultimate-overlay"))
        {
            autoCaptureService.TryUseUltimate();
        }
        ImGui.SameLine();
        ImGui.TextDisabled(autoCaptureService.ManualActionStatus);
    }

    private static void DrawAdvancedCandidate(string type, string actionName, bool enabled, string reason)
    {
        ImGui.Text($"{type}：{actionName}");
        ImGui.SameLine();
        ImGui.TextDisabled(enabled ? reason : "Toggle is off");
    }

    private void DrawSidebar()
    {
        ImGui.Text("Beastmaster Assistant");
        ImGui.TextDisabled("Beastmaster Progress Hub");
        ImGui.Separator();

        DrawSidebarLabel("Content");
        DrawSidebarButton(MainSections[0]);
        DrawSidebarButton(MainSections[1]);
        DrawSidebarButton(MainSections[2]);
        DrawSidebarButton(MainSections[3]);
        DrawSidebarButton(MainSections[4]);
        DrawSidebarButton(MainSections[5]);
        DrawSidebarButton(MainSections[6]);
        DrawSidebarButton(MainSections[7]);
        DrawSidebarButton(MainSections[8]);

        ImGui.Separator();
        DrawSidebarLabel("Tools");
        DrawSidebarButton(MainSections[9]);

        if (ImGui.Button("Feedback & Suggestions", new Vector2(ImGui.GetContentRegionAvail().X, 30f)))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                "https://discord.com/channels/1258981591124938762/1546882305686118420")
            {
                UseShellExecute = true,
            });
        }

        DrawSidebarButton(MainSections[10]);
        DrawSidebarButton(MainSections[11]);
    }

    private void DrawSidebarButton((string Key, string Label) section)
    {
        var selected = configuration.SelectedMainSection == section.Key;
        var hasSectionColor = section.Key is "catalog" or "party";
        if (hasSectionColor)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, section.Key == "catalog"
                ? new Vector4(0.42f, 0.30f, 0.10f, 1f)
                : new Vector4(0.42f, 0.22f, 0.14f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, section.Key == "catalog"
                ? new Vector4(0.58f, 0.42f, 0.14f, 1f)
                : new Vector4(0.58f, 0.30f, 0.20f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, section.Key == "catalog"
                ? new Vector4(0.65f, 0.48f, 0.18f, 1f)
                : new Vector4(0.66f, 0.35f, 0.24f, 1f));
        }
        if (selected)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, section.Key == "catalog"
                ? new Vector4(0.62f, 0.44f, 0.12f, 1f)
                : section.Key == "party"
                    ? new Vector4(0.64f, 0.32f, 0.20f, 1f)
                    : new Vector4(0.12f, 0.23f, 0.25f, 1f));
        }

        if (ImGui.Button($"{section.Label}##section-{section.Key}", new Vector2(ImGui.GetContentRegionAvail().X, 34f)))
        {
            configuration.SelectedMainSection = section.Key;
            configuration.Save();
            if (section.Key == "arena-navigation")
            {
                navigationService.Navigate(new BeastmasterQuestLocation(
                    148,
                    4,
                    new Vector3(24.238f, -6.003f, 65.809f),
                    "Central Shroud",
                    "Beast Arena"));
            }
        }

        if (selected)
        {
            ImGui.PopStyleColor();
        }
        if (hasSectionColor)
        {
            ImGui.PopStyleColor(3);
        }
    }

    private static void DrawSidebarLabel(string label)
    {
        ImGui.Spacing();
        ImGui.TextDisabled(label);
    }

    private void DrawArenaNavigation()
    {
        ImGui.Text("Go to the Beast Arena");
        ImGui.TextDisabled("Automatically navigates to the Beast Arena entrance in Central Shroud.");
        ImGui.Separator();
        ImGui.Text("Type: Current Location");
        ImGui.TextDisabled("TerritoryType: 148");
        ImGui.TextDisabled("Zone: Central Shroud");
        ImGui.TextDisabled("Map.RowId: 4");
        ImGui.TextDisabled("World Coordinates: X=24.238, Y=-6.003, Z=65.809");
        ImGui.TextDisabled("Requires vnavmesh; cross-zone travel requires Lifestream");
    }

    private void DrawContent()
    {
        switch (configuration.SelectedMainSection)
        {
            case "quests":
                DrawQuests();
                break;
            case "catalog":
                DrawCatalog();
                break;
            case "party":
                DrawBeastArena();
                break;
            case "equipment":
                DrawEquipment();
                break;
            case "combinations":
                DrawRecommendedCombinations();
                break;
            case "sequences":
                DrawSequenceEditor();
                break;
            case "rules":
                DrawRuleEditor();
                break;
            case "commands":
                DrawCommands();
                break;
            case "auto-output":
                DrawAutoOutput();
                break;
            case "settings":
                DrawSettings();
                break;
            case "debug":
                DrawDebug();
                break;
            case "arena-navigation":
                DrawArenaNavigation();
                break;
            default:
                configuration.SelectedMainSection = "quests";
                DrawQuests();
                break;
        }
    }

    private static void DrawRecommendedCombinations()
    {
        ImGui.TextColored(new Vector4(1f, 0.35f, 0.25f, 1f), "Do the main quest line first — leveling gets much easier once you have the catch-up gear!!!");
        ImGui.Text("Recommended Team Comps"); ImGui.SameLine();
        ImGui.TextDisabled("Static guide only, not wired into Auto Rotation");
        ImGui.Separator();

        ImGui.Text("Bug Team · Floors 1-2 Speed Clear");
        ImGui.TextColored(new Vector4(0.95f, 0.82f, 0.25f, 1f), "Recommended lineup: Mantis + Hornet + Kusi");
        ImGui.TextWrapped("The simplest, most brainless speed-clear lineup. The key is making sure the mantis's buff skill doesn't whiff.");
        DrawCombinationStep("Whistle 1 summons the Mantis, applying physical vulnerability", true, ", then use");
        DrawCombinationStep("Whistle 2 summons the Hornet, use", false, "to self-destruct.");
        DrawCombinationStep("Whistle 3 summons Kusi, use", false, ", then attack normally.");

        ImGui.Separator();
        ImGui.Text("Bug Team · High Floor 1 Speed Clear");
        ImGui.TextColored(new Vector4(0.95f, 0.82f, 0.25f, 1f), "Recommended lineup: Mantis + Hummingbird + Hornet");
        DrawCombinationStep("Whistle 1 summons the Mantis, applying physical vulnerability", true, ", then use");
        DrawCombinationStep("Whistle 2 summons the Hummingbird, use", true, ""Six-Hit Kick" (single target), then use");
        DrawCombinationStep("Whistle 3 summons the Hornet; once the boss's HP drops below 25%, use", false, "。");

        ImGui.Separator();
        ImGui.Text("Water Team · Reference Lineup");
        ImGui.TextColored(new Vector4(0.35f, 0.75f, 1f, 1f), "Newt + Giant Land Crab + Shellcrab");
        ImGui.TextWrapped("The core idea is that the Newt and Giant Land Crab each apply magic and water vulnerability, while the third slot, Shellcrab, handles the main damage.");
        ImGui.TextDisabled("Before the fight, switch to Whistle 3 to prep the Shellcrab's Borrow; try to land Aquatic Wave inside the vulnerability window, which can also dispel enemy buffs.");
        DrawCombinationStep("Whistle 1 summons the Newt, applying magic vulnerability", true, ", then use");
        DrawCombinationStep("Whistle 2 summons the Giant Land Crab, applying water vulnerability", true, ", perform a combo, then use");
        DrawCombinationStep("Whistle 3 summons the Shellcrab", false, ", then attack normally.");

        ImGui.Text("Water Team · Floors 1-2 Speed Clear Variant");
        ImGui.TextColored(new Vector4(0.35f, 0.75f, 1f, 1f), "Newt + Giant Land Crab + Jade Penguin");
        ImGui.TextDisabled("Before the fight, switch to Whistle 3 to prep the Jade Penguin's Borrow; the Land Crab doesn't need to wait for the full combo.");
        DrawCombinationStep("Whistle 1 summons the Newt, applying magic vulnerability", true, ", then use");
        DrawCombinationStep("Whistle 2 summons the Giant Land Crab, applying water vulnerability", true, ", no need to wait for the full combo, use directly");
        DrawCombinationStep("Whistle 3 summons the Jade Penguin", false, ", then attack normally.");

        ImGui.Separator();
        ImGui.Text("Credits & Sources");
        ImGui.Text("Submitted by: community member Dasanyuan");
        ImGui.Text("Water Team reference video: Yefeng");
        if (ImGui.Button("Open the Bilibili Reference Video##recommended-combination-bilibili"))
        {
            Process.Start(new ProcessStartInfo(
                "https://www.bilibili.com/video/BV1c1Y46AEnN/?spm_id_from=333.788.videopod.sections&vd_source=e8d743edd1edd93f4c56cdcf6833f6ff&p=2")
            {
                UseShellExecute = true,
            });
        }
        ImGui.SameLine();
        ImGui.TextDisabled("Opens in your default browser when clicked");
    }

    private static void DrawCombinationStep(string prefix, bool finalStrike, string suffix)
    {
        ImGui.Bullet();
        ImGui.SameLine();
        ImGui.TextUnformatted(prefix);
        ImGui.SameLine(0f, 3f);
        DrawCombinationAction("[Release]", 44890, new Vector4(1f, 0.82f, 0.25f, 1f));
        if (finalStrike)
        {
            ImGui.SameLine(0f, 3f);
            ImGui.TextUnformatted(suffix);
            ImGui.SameLine(0f, 3f);
            DrawCombinationAction("[Finishing Blow]", 44891, new Vector4(1f, 0.4f, 0.3f, 1f));
            ImGui.SameLine(0f, 3f);
            ImGui.TextUnformatted("。");
        }
        else
        {
            ImGui.SameLine(0f, 3f);
            ImGui.TextUnformatted(suffix);
        }
    }

    private static void DrawCombinationAction(string label, uint actionId, Vector4 color)
    {
        ImGui.TextColored(color, label);
        if (!ImGui.IsItemHovered())
        {
            return;
        }

        ImGui.BeginTooltip();
        DrawActionTooltip("Skill", actionId);
        var description = actionId switch
        {
            44890u => "Has the current summoned beast use its Release skill on the target; the actual skill changes based on the current beast.",
            44891u => "Has the current summoned beast use Finishing Blow on the target.",
            _ => string.Empty,
        };
        if (!string.IsNullOrWhiteSpace(description))
        {
            ImGui.Separator();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 28f);
            ImGui.TextWrapped(description);
            ImGui.PopTextWrapPos();
        }
        ImGui.EndTooltip();
    }

    private void DrawSequenceEditor()
    {
        ImGui.Text("Skill Sequence");
        ImGui.TextDisabled("Edit the countdown and combat steps; the sequence won't run automatically unless enabled in Auto Rotation.");
        ImGui.Separator();

        var sequences = configuration.Sequences;
        if (sequences.Count == 0)
        {
            sequences.Add(BeastmasterSequenceDefinition.CreateWaterOpener());
        }

        var selected = Math.Clamp(configuration.SelectedSequenceIndex, 0, sequences.Count - 1);
        var names = string.Join('\0', sequences.Select(sequence => sequence.Name)) + '\0';
        if (ImGui.Combo("Current Sequence", ref selected, names))
        {
            configuration.SelectedSequenceIndex = selected;
            configuration.Save();
        }

        ImGui.SameLine();
        var sequenceChat = sequenceService.ChatMessagesEnabled;
        if (ImGui.Checkbox("Sequence Diagnostics", ref sequenceChat))
        {
            sequenceService.SetChatMessagesEnabled(sequenceChat);
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Logs sequence start, skill requests, completion, and abort events; failed retries won't spam the log.");
        }

        var sequence = sequences[selected];
        var name = sequence.Name;
        ImGui.SetNextItemWidth(260f);
        if (ImGui.InputText("Name", ref name, 80))
        {
            sequence.Name = string.IsNullOrWhiteSpace(name) ? "Unnamed Sequence" : name;
            configuration.Save();
        }
        var description = sequence.Description;
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.InputText("Description", ref description, 200))
        {
            sequence.Description = description;
            configuration.Save();
        }

        if (ImGui.Button("New Sequence"))
        {
            sequences.Add(new BeastmasterSequenceDefinition { Name = "New Sequence", Description = "" });
            configuration.SelectedSequenceIndex = sequences.Count - 1;
            configuration.Save();
        }
        ImGui.SameLine();
        if (ImGui.Button("Duplicate Sequence"))
        {
            var copy = BeastmasterSequenceDefinition.TryImport(sequence.Export(), out var imported, out _)
                ? imported!
                : BeastmasterSequenceDefinition.CreateWaterOpener();
            copy.Name += " Copy";
            sequences.Add(copy);
            configuration.SelectedSequenceIndex = sequences.Count - 1;
            configuration.Save();
        }
        ImGui.SameLine();
        if (ImGui.Button("Delete Sequence") && sequences.Count > 1)
        {
            sequences.RemoveAt(selected);
            configuration.SelectedSequenceIndex = Math.Clamp(selected, 0, sequences.Count - 1);
            configuration.Save();
        }
        ImGui.SameLine();
        if (ImGui.Button("Restore Bug Team Template"))
        {
            sequences[selected] = BeastmasterSequenceDefinition.CreateWaterOpener();
            configuration.Save();
        }

        DrawSequenceStepList("Countdown", sequence.CountdownSteps, true);
        DrawSequenceStepList("Enter Combat", sequence.CombatSteps, false);

        if (ImGui.Button("Copy Export Text")) ImGui.SetClipboardText(sequence.Export());
        ImGui.SameLine();
        if (ImGui.Button("Import from Clipboard"))
        {
            if (BeastmasterSequenceDefinition.TryImport(ImGui.GetClipboardText(), out var imported, out var error) && imported != null)
            {
                sequences[selected] = imported;
                configuration.Save();
            }
            else debugResult = $"Failed to import skill sequence: {error}";
        }
    }

    private void DrawSequenceStepList(string title, List<BeastmasterSequenceStep> steps, bool countdown)
    {
        if (!ImGui.CollapsingHeader($"{title} Steps##sequence-{title}")) return;
        for (var index = 0; index < steps.Count; index++)
        {
            var step = steps[index];
            ImGui.PushID($"{title}-{index}");
            ImGui.Text($"{index + 1}.");
            ImGui.SameLine();
            if (countdown)
            {
                var time = step.TimeSeconds ?? 0f;
                ImGui.SetNextItemWidth(110f);
                if (ImGui.InputFloat("Time", ref time, 1f, 5f, "T-%.1f")) step.TimeSeconds = time;
                ImGui.SameLine();
            }
            var actionIndex = Array.FindIndex(SequenceActions, action => action.ActionId == step.ActionId);
            var actionOptions = string.Join('\0', SequenceActions.Select(action => action.Name)) + '\0';
            if (actionIndex < 0)
            {
                ImGui.TextDisabled($"Unknown skill ({step.ActionId})");
                ImGui.SameLine();
            }
            else
            {
                ImGui.SetNextItemWidth(180f);
                if (ImGui.Combo("Skill", ref actionIndex, actionOptions))
                {
                    step.ActionId = SequenceActions[actionIndex].ActionId;
                    step.Label = SequenceActions[actionIndex].Name;
                }
                ImGui.SameLine();
            }
            ImGui.SameLine();
            if (ImGui.SmallButton("Move Up") && index > 0) (steps[index - 1], steps[index]) = (steps[index], steps[index - 1]);
            ImGui.SameLine();
            if (ImGui.SmallButton("Move Down") && index < steps.Count - 1) (steps[index + 1], steps[index]) = (steps[index], steps[index + 1]);
            ImGui.SameLine();
            if (ImGui.SmallButton("Delete")) { steps.RemoveAt(index); ImGui.PopID(); break; }
            ImGui.PopID();
        }
        if (ImGui.Button($"Add {title} Step")) steps.Add(new(countdown ? 0f : null, 44879, "Shattering Slash"));
    }

    private void DrawRuleEditor()
    {
        ImGui.Text("Rule Mode");
        ImGui.TextDisabled("Rules only run in combat; they take priority below the skill sequence but above normal ACR, and are checked in display order, using the first match.");
        ImGui.Separator();

        var enabled = ruleService.Enabled;
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.82f, 0.25f, 1f));
        if (ImGui.Checkbox("Enable Rule Mode", ref enabled)) ruleService.SetEnabled(enabled);
        ImGui.PopStyleColor();
        var diagnosticsEnabled = configuration.RuleDiagnosticsEnabled;
        if (ImGui.Checkbox("Rule Diagnostics", ref diagnosticsEnabled))
        {
            configuration.RuleDiagnosticsEnabled = diagnosticsEnabled;
            configuration.Save();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("When enabled, logs rule success/failure according to the current rule set's diagnostic level; off by default.");
        }

        var ruleSets = configuration.RuleSets;
        if (ruleSets.Count == 0)
        {
            ruleSets.Add(new BeastmasterRuleSetDefinition());
        }
        configuration.SelectedRuleSetIndex = Math.Clamp(configuration.SelectedRuleSetIndex, 0, ruleSets.Count - 1);

        ImGui.BeginChild("RuleSetList", new Vector2(220f, 0f), true);
        ImGui.Text("Rule Set");
        for (var index = 0; index < ruleSets.Count; index++)
        {
            var label = $"{index + 1:00} {(ruleSets[index].Enabled ? "[On]" : "[Off]")} {ruleSets[index].Name}";
            if (ImGui.Selectable($"{label}##rule-set-{index}", configuration.SelectedRuleSetIndex == index))
            {
                configuration.SelectedRuleSetIndex = index;
                configuration.SelectedRuleIndex = 0;
                configuration.Save();
            }
        }
        ImGui.Separator();
        if (ImGui.Button("New Rule Set", new Vector2(-1f, 0f)))
        {
            ruleSets.Add(new BeastmasterRuleSetDefinition { Name = "New Rule Set" });
            configuration.SelectedRuleSetIndex = ruleSets.Count - 1;
            configuration.SelectedRuleIndex = 0;
            configuration.Save();
        }
        if (ImGui.Button("Duplicate Rule Set", new Vector2(-1f, 0f)))
        {
            var source = ruleSets[configuration.SelectedRuleSetIndex];
            if (BeastmasterRuleSetDefinition.TryImport(source.Export(), out var copy, out _) && copy != null)
            {
                copy.Name += " Copy";
                ruleSets.Add(copy);
                configuration.SelectedRuleSetIndex = ruleSets.Count - 1;
                configuration.SelectedRuleIndex = 0;
                configuration.Save();
            }
        }
        if (ImGui.Button("Delete Rule Set", new Vector2(-1f, 0f)) && ruleSets.Count > 1)
        {
            ruleSets.RemoveAt(configuration.SelectedRuleSetIndex);
            configuration.SelectedRuleSetIndex = Math.Clamp(configuration.SelectedRuleSetIndex, 0, ruleSets.Count - 1);
            configuration.SelectedRuleIndex = 0;
            configuration.Save();
        }
        ImGui.EndChild();

        ImGui.SameLine();
        ImGui.BeginChild("RuleSetEditor", Vector2.Zero, true);
        var ruleSetIndex = configuration.SelectedRuleSetIndex;
        var ruleSet = ruleSets[ruleSetIndex];
        DrawRuleSetSettings(ruleSet, ruleSetIndex);
        ImGui.Separator();
        DrawRuleList(ruleSet);
        ImGui.Separator();
        ImGui.Text("Recent Diagnostics");
        ImGui.TextWrapped(ruleService.LastDiagnostic);
        ImGui.TextDisabled(ruleService.LastDiagnosticUtc == DateTime.MinValue
            ? "No run history yet"
            : ruleService.LastDiagnosticUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff"));
        if (!string.IsNullOrWhiteSpace(ruleImportStatus)) ImGui.TextWrapped(ruleImportStatus);
        ImGui.EndChild();
    }

    private void DrawRuleSetSettings(BeastmasterRuleSetDefinition ruleSet, int ruleSetIndex)
    {
        var enabled = ruleSet.Enabled;
        if (ImGui.Checkbox("Enable Current Rule Set", ref enabled))
        {
            ruleSet.Enabled = enabled;
            configuration.Save();
        }
        var name = ruleSet.Name;
        ImGui.SetNextItemWidth(260f);
        if (ImGui.InputText("Name", ref name, 80))
        {
            ruleSet.Name = string.IsNullOrWhiteSpace(name) ? "Unnamed Rule Set" : name;
            configuration.Save();
        }
        var description = ruleSet.Description;
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.InputText("Description", ref description, 200))
        {
            ruleSet.Description = description;
            configuration.Save();
        }

        var areaMode = (int)ruleSet.AreaMode;
        ImGui.SetNextItemWidth(180f);
        if (ImGui.Combo("Zone Restriction", ref areaMode, "All Zones\0Only Specified Zones\0Exclude Specified Zones\0"))
        {
            ruleSet.AreaMode = (BeastmasterRuleAreaMode)areaMode;
            configuration.Save();
        }
        if (ruleSet.AreaMode != BeastmasterRuleAreaMode.All)
        {
            ImGui.TextDisabled(ruleSet.TerritoryIds.Count == 0 ? "No TerritoryType ID configured yet" : $"TerritoryType：{string.Join(", ", ruleSet.TerritoryIds)}");
            ImGui.SetNextItemWidth(130f);
            if (ImGui.InputInt("New Zone ID", ref newRuleTerritoryId, 1, 10)) newRuleTerritoryId = Math.Max(0, newRuleTerritoryId);
            ImGui.SameLine();
            if (ImGui.Button("Add Zone") && newRuleTerritoryId is > 0 and <= ushort.MaxValue)
            {
                var territoryId = (ushort)newRuleTerritoryId;
                if (!ruleSet.TerritoryIds.Contains(territoryId)) ruleSet.TerritoryIds.Add(territoryId);
                newRuleTerritoryId = 0;
                configuration.Save();
            }
            for (var index = 0; index < ruleSet.TerritoryIds.Count; index++)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton($"Delete {ruleSet.TerritoryIds[index]}##territory-{index}"))
                {
                    ruleSet.TerritoryIds.RemoveAt(index);
                    configuration.Save();
                    break;
                }
            }
        }

        var diagnosticMode = (int)ruleSet.DiagnosticMode;
        ImGui.SetNextItemWidth(180f);
        if (ImGui.Combo("Rule Diagnostic Level", ref diagnosticMode, "Off\0Failures Only\0Full\0"))
        {
            ruleSet.DiagnosticMode = (BeastmasterRuleDiagnosticMode)diagnosticMode;
            configuration.Save();
        }

        if (ImGui.Button("Copy Export Text"))
        {
            ImGui.SetClipboardText(ruleSet.Export());
            ruleImportStatus = "Current rule set copied to clipboard.";
        }
        ImGui.SameLine();
        if (ImGui.Button("Import from Clipboard"))
        {
            if (BeastmasterRuleSetDefinition.TryImport(ImGui.GetClipboardText(), out var imported, out var error) && imported != null)
            {
                configuration.RuleSets.Add(imported);
                configuration.SelectedRuleSetIndex = configuration.RuleSets.Count - 1;
                configuration.SelectedRuleIndex = 0;
                configuration.Save();
                ruleImportStatus = $"Imported new rule set "{imported.Name}".";
            }
            else
            {
                ruleImportStatus = $"Failed to import rule set: {error}";
            }
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("Move Rule Set Up") && ruleSetIndex > 0)
        {
            (configuration.RuleSets[ruleSetIndex - 1], configuration.RuleSets[ruleSetIndex]) = (configuration.RuleSets[ruleSetIndex], configuration.RuleSets[ruleSetIndex - 1]);
            configuration.SelectedRuleSetIndex--;
            configuration.Save();
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("Move Rule Set Down") && ruleSetIndex < configuration.RuleSets.Count - 1)
        {
            (configuration.RuleSets[ruleSetIndex + 1], configuration.RuleSets[ruleSetIndex]) = (configuration.RuleSets[ruleSetIndex], configuration.RuleSets[ruleSetIndex + 1]);
            configuration.SelectedRuleSetIndex++;
            configuration.Save();
        }
    }

    private void DrawRuleList(BeastmasterRuleSetDefinition ruleSet)
    {
        ImGui.Text($"Rules ({ruleSet.Rules.Count}/100)");
        if (ImGui.Button("Add Rule") && ruleSet.Rules.Count < 100)
        {
            ruleSet.Rules.Add(new BeastmasterRuleDefinition());
            configuration.SelectedRuleIndex = ruleSet.Rules.Count - 1;
            configuration.Save();
        }

        if (ruleSet.Rules.Count == 0)
        {
            ImGui.TextDisabled("This rule set has no rules yet. Once added, they're evaluated top to bottom.");
            return;
        }

        configuration.SelectedRuleIndex = Math.Clamp(configuration.SelectedRuleIndex, 0, ruleSet.Rules.Count - 1);
        for (var index = 0; index < ruleSet.Rules.Count; index++)
        {
            var rule = ruleSet.Rules[index];
            var selected = configuration.SelectedRuleIndex == index;
            if (ImGui.Selectable($"{index + 1:00} {(rule.Enabled ? "[On]" : "[Off]")} {rule.Name}  |  {GetRuleSummary(rule)}##rule-{index}", selected))
            {
                configuration.SelectedRuleIndex = index;
                configuration.Save();
            }
        }

        var selectedIndex = configuration.SelectedRuleIndex;
        var selectedRule = ruleSet.Rules[selectedIndex];
        ImGui.Spacing();
        ImGui.Text($"Editing Rule {selectedIndex + 1}");
        DrawRuleFields(selectedRule);

        if (ImGui.Button("Duplicate Rule") && ruleSet.Rules.Count < 100)
        {
            ruleSet.Rules.Insert(selectedIndex + 1, CloneRule(selectedRule));
            configuration.SelectedRuleIndex++;
            configuration.Save();
        }
        ImGui.SameLine();
        if (ImGui.Button("Move Up") && selectedIndex > 0)
        {
            (ruleSet.Rules[selectedIndex - 1], ruleSet.Rules[selectedIndex]) = (ruleSet.Rules[selectedIndex], ruleSet.Rules[selectedIndex - 1]);
            configuration.SelectedRuleIndex--;
            configuration.Save();
        }
        ImGui.SameLine();
        if (ImGui.Button("Move Down") && selectedIndex < ruleSet.Rules.Count - 1)
        {
            (ruleSet.Rules[selectedIndex + 1], ruleSet.Rules[selectedIndex]) = (ruleSet.Rules[selectedIndex], ruleSet.Rules[selectedIndex + 1]);
            configuration.SelectedRuleIndex++;
            configuration.Save();
        }
        ImGui.SameLine();
        if (ImGui.Button("Delete Rule"))
        {
            ruleSet.Rules.RemoveAt(selectedIndex);
            configuration.SelectedRuleIndex = Math.Clamp(selectedIndex, 0, Math.Max(0, ruleSet.Rules.Count - 1));
            configuration.Save();
        }
    }

    private void DrawRuleFields(BeastmasterRuleDefinition rule)
    {
        var enabled = rule.Enabled;
        if (ImGui.Checkbox("Enabled##selected-rule", ref enabled))
        {
            rule.Enabled = enabled;
            configuration.Save();
        }
        var name = rule.Name;
        ImGui.SetNextItemWidth(260f);
        if (ImGui.InputText("Rule Name", ref name, 80))
        {
            rule.Name = string.IsNullOrWhiteSpace(name) ? "Unnamed Rule" : name;
            configuration.Save();
        }

        rule.EnsureConditions();
        var joinMode = (int)rule.ConditionJoinMode;
        ImGui.SetNextItemWidth(190f);
        if (ImGui.Combo("Condition Logic", ref joinMode, "All Must Match (AND)\0Any Can Match (OR)\0"))
        {
            rule.ConditionJoinMode = (BeastmasterRuleConditionJoinMode)joinMode;
            configuration.Save();
        }

        for (var conditionIndex = 0; conditionIndex < rule.Conditions.Count; conditionIndex++)
        {
            ImGui.Separator();
            ImGui.Text($"Condition {conditionIndex + 1}");
            DrawRuleConditionFields(rule, rule.Conditions[conditionIndex], conditionIndex);
            if (rule.Conditions.Count > 1 && ImGui.Button($"Delete Condition##rule-condition-delete-{conditionIndex}"))
            {
                rule.Conditions.RemoveAt(conditionIndex);
                rule.SyncLegacyFieldsFromFirstCondition();
                configuration.Save();
                break;
            }
        }

        ImGui.BeginDisabled(rule.Conditions.Count >= 10);
        if (ImGui.Button("Add Condition"))
        {
            rule.Conditions.Add(new BeastmasterRuleCondition());
            configuration.Save();
        }
        ImGui.EndDisabled();

        var actionType = (int)rule.ActionType;
        ImGui.SetNextItemWidth(120f);
        if (ImGui.Combo("Execution Type", ref actionType, "Skill\0Trial Item\0"))
        {
            rule.ActionType = (BeastmasterRuleActionType)actionType;
            configuration.Save();
        }

        if (rule.ActionType == BeastmasterRuleActionType.CrucibleItem)
        {
            var itemType = (int)rule.CrucibleItemType;
            ImGui.SetNextItemWidth(190f);
            if (ImGui.Combo("Trial Item", ref itemType, "Recovery Items\0Assorted Fangs\0Book of Evasion\0Book of Reflection\0Sands of Time\0Beast Vigor Potion\0Vampire Fang\0"))
            {
                rule.CrucibleItemType = (BeastmasterCrucibleItemType)itemType;
                configuration.Save();
            }
        }
        else
        {
            var actionIndex = Array.FindIndex(BeastmasterRuleActions.Supported, action => action.ActionId == rule.ActionId);
            if (actionIndex < 0) actionIndex = 0;
            var actionNames = string.Join('\0', BeastmasterRuleActions.Supported.Select(action => $"{action.Name} ({action.ActionId})")) + '\0';
            ImGui.SetNextItemWidth(260f);
            if (ImGui.Combo("Select Skill", ref actionIndex, actionNames))
            {
                rule.ActionId = BeastmasterRuleActions.Supported[actionIndex].ActionId;
                configuration.Save();
            }
        }

        if (!rule.TryValidate(out var error)) ImGui.TextColored(new Vector4(1f, 0.4f, 0.3f, 1f), error);
        ImGui.TextDisabled("The chosen skill is always used on your current manual target; the DataID object is only used as the trigger. If the skill fails, it falls back to ACR.");
    }

    private void DrawRuleConditionFields(BeastmasterRuleDefinition rule, BeastmasterRuleCondition condition, int index)
    {
        var type = (int)condition.Type;
        ImGui.SetNextItemWidth(190f);
        if (ImGui.Combo($"Check Type##condition-{index}", ref type, "Self Buff\0Target Buff\0DataID Buff\0DataID Casting\0Target Casting\0Target DataID\0Self HP\0Target HP\0Target Is Boss (IsBoss)\0"))
        {
            condition.Type = (BeastmasterRuleConditionType)type;
            rule.SyncLegacyFieldsFromFirstCondition();
            configuration.Save();
        }

        if (condition.IsStatusRule)
        {
            var statusCondition = (int)condition.StatusCondition;
            ImGui.SetNextItemWidth(190f);
            if (ImGui.Combo($"Buff Condition##condition-{index}", ref statusCondition, "Present\0Missing\0"))
            {
                condition.StatusCondition = (BeastmasterRuleStatusCondition)statusCondition;
                rule.SyncLegacyFieldsFromFirstCondition();
                configuration.Save();
            }
        }

        if (condition.RequiresDataId)
        {
            var dataId = (int)Math.Min(condition.DataId, int.MaxValue);
            ImGui.SetNextItemWidth(190f);
            if (ImGui.InputInt($"DataID##condition-{index}", ref dataId, 1, 100))
            {
                condition.DataId = (uint)Math.Max(0, dataId);
                rule.SyncLegacyFieldsFromFirstCondition();
                configuration.Save();
            }
        }

        if (condition.IsHealthRule)
        {
            var hpCondition = (int)condition.HpCondition;
            ImGui.SetNextItemWidth(190f);
            if (ImGui.Combo($"HP Condition##condition-{index}", ref hpCondition, "Greater Than\0Less Than\0"))
            {
                condition.HpCondition = (BeastmasterRuleHpCondition)hpCondition;
                rule.SyncLegacyFieldsFromFirstCondition();
                configuration.Save();
            }

            var threshold = condition.HpThreshold;
            ImGui.SetNextItemWidth(190f);
            if (ImGui.InputFloat($"HP Threshold##condition-{index}", ref threshold, 0f, 0f, "%.0f%%"))
            {
                condition.HpThreshold = Math.Clamp(threshold, 1f, 100f);
                rule.SyncLegacyFieldsFromFirstCondition();
                configuration.Save();
            }
        }
        else if (condition.Type is not (BeastmasterRuleConditionType.TargetDataId or BeastmasterRuleConditionType.TargetIsBoss))
        {
            var conditionId = (int)Math.Min(condition.ConditionId, int.MaxValue);
            ImGui.SetNextItemWidth(190f);
            var label = condition.IsStatusRule ? "BUFFID" : "Casting ID";
            if (ImGui.InputInt($"{label}##condition-{index}", ref conditionId, 1, 100))
            {
                condition.ConditionId = (uint)Math.Max(0, conditionId);
                rule.SyncLegacyFieldsFromFirstCondition();
                configuration.Save();
            }
        }
    }

    private static BeastmasterRuleDefinition CloneRule(BeastmasterRuleDefinition source)
        => new()
        {
            Enabled = source.Enabled,
            Name = source.Name + " Copy",
            ConditionType = source.ConditionType,
            StatusCondition = source.StatusCondition,
            ConditionJoinMode = source.ConditionJoinMode,
            Conditions = source.Conditions.Select(condition => new BeastmasterRuleCondition
            {
                Type = condition.Type,
                StatusCondition = condition.StatusCondition,
                DataId = condition.DataId,
                ConditionId = condition.ConditionId,
                HpCondition = condition.HpCondition,
                HpThreshold = condition.HpThreshold,
            }).ToList(),
            DataId = source.DataId,
            ConditionId = source.ConditionId,
            HpCondition = source.HpCondition,
            HpThreshold = source.HpThreshold,
            ActionType = source.ActionType,
            ActionId = source.ActionId,
            CrucibleItemType = source.CrucibleItemType,
            CrucibleItemId = source.CrucibleItemId,
        };

    private static string GetRuleSummary(BeastmasterRuleDefinition rule)
    {
        rule.EnsureConditions();
        var condition = string.Join(
            rule.ConditionJoinMode == BeastmasterRuleConditionJoinMode.All ? " AND " : " OR ",
            rule.Conditions.Select(GetRuleConditionSummary));
        if (rule.ActionType == BeastmasterRuleActionType.CrucibleItem)
            return $"{condition} -> Item {BeastmasterRuleActions.GetCrucibleItemTypeName(rule.CrucibleItemType)}";
        var action = BeastmasterRuleActions.Supported.FirstOrDefault(item => item.ActionId == rule.ActionId);
        return $"{condition} -> {(string.IsNullOrEmpty(action.Name) ? rule.ActionId.ToString() : action.Name)}";
    }

    private static string GetRuleConditionSummary(BeastmasterRuleCondition condition)
    {
        var actor = condition.Type switch
        {
            BeastmasterRuleConditionType.SelfStatus => "Self",
            BeastmasterRuleConditionType.TargetStatus => "Target",
            BeastmasterRuleConditionType.DataIdStatus => $"DataID {condition.DataId}",
            BeastmasterRuleConditionType.DataIdCast => $"DataID {condition.DataId}",
            BeastmasterRuleConditionType.TargetCast => "Target",
            BeastmasterRuleConditionType.TargetDataId => $"Target DataID {condition.DataId}",
            BeastmasterRuleConditionType.SelfHp => "Self HP",
            BeastmasterRuleConditionType.TargetHp => "Target HP",
            BeastmasterRuleConditionType.TargetIsBoss => "Target Is Boss",
            _ => "Unknown",
        };
        if (condition.IsStatusRule)
            return $"{actor}{(condition.StatusCondition == BeastmasterRuleStatusCondition.Present ? "present" : "missing")} BUFF {condition.ConditionId}";
        if (condition.Type == BeastmasterRuleConditionType.TargetDataId)
            return actor;
        if (condition.Type == BeastmasterRuleConditionType.TargetIsBoss)
            return "Target's Max HP > Self's Max HP × 5";
        if (condition.IsHealthRule)
            return $"{actor} {(condition.HpCondition == BeastmasterRuleHpCondition.Above ? ">" : "<")} {condition.HpThreshold:0.#}%";
        return $"{actor} casting {condition.ConditionId}";
    }

    private void DrawQuests()
    {
        ImGui.Text("Beastmaster Quest Chain");
        ImGui.SameLine();
        if (ImGui.Button("WIKI##quest-wiki"))
        {
            Process.Start(new ProcessStartInfo("https://ff14.huijiwiki.com/wiki/%E9%A9%AF%E5%85%BD%E5%B8%88#%E7%89%B9%E8%81%8C%E4%BB%BB%E5%8A%A1")
            {
                UseShellExecute = true,
            });
        }

        ImGui.SameLine();
        var stopButtonWidth = ImGui.CalcTextSize("Stop Navigation").X + ImGui.GetStyle().FramePadding.X * 2f;
        var stopButtonX = ImGui.GetWindowContentRegionMax().X - stopButtonWidth;
        if (ImGui.GetCursorPosX() < stopButtonX)
        {
            ImGui.SetCursorPosX(stopButtonX);
        }

        if (ImGui.Button("Stop Navigation"))
        {
            navigationService.Stop();
        }

        ImGui.TextDisabled("Shows progress based on the client's quest status and navigates to pickup locations for incomplete quests.");
        ImGui.Separator();

        var quests = BeastmasterQuestGuide.Quests;
        var identifiedQuests = quests.Where(quest => quest.RowId != 0).ToArray();
        var statusRowIds = identifiedQuests
            .SelectMany(quest => questService.GetPrerequisites(quest.RowId).Select(previous => previous.RowId).Append(quest.RowId))
            .Distinct()
            .ToArray();
        if (DateTime.UtcNow >= nextQuestStatusRefreshUtc)
        {
            questService.RefreshStatuses(statusRowIds);
            nextQuestStatusRefreshUtc = DateTime.UtcNow.AddSeconds(2);
        }

        var completedCount = identifiedQuests.Count(quest => questService.GetStatus(quest.RowId) == BeastmasterQuestStatus.Completed);
        ImGui.ProgressBar((float)completedCount / quests.Count, new Vector2(-1f, 0f), $"{completedCount}/{quests.Count}");
        ImGui.Spacing();

        var hideCompleted = configuration.HideCompletedQuests;
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.82f, 0.25f, 1f));
        if (ImGui.Checkbox("Hide Completed Quests", ref hideCompleted))
        {
            configuration.HideCompletedQuests = hideCompleted;
            configuration.Save();
        }
        ImGui.PopStyleColor();
        ImGui.Separator();

        foreach (var quest in quests)
        {
            if (quest.RowId == 0)
            {
                ImGui.Text($"{quest.Name}  [Coordinates Pending]");
                ImGui.TextDisabled(quest.Summary);
                ImGui.BeginDisabled();
                ImGui.Button("Navigate to Start NPC##issuer-pending");
                ImGui.SameLine();
                ImGui.Button("Navigate to Quest Objective##target-pending");
                ImGui.EndDisabled();
                ImGui.Separator();
                continue;
            }

            var questStatus = questService.GetStatus(quest.RowId);
            var completed = questStatus == BeastmasterQuestStatus.Completed;
            if (completed && configuration.HideCompletedQuests)
            {
                continue;
            }

            var status = completed
                ? "Completed"
                : questStatus == BeastmasterQuestStatus.Accepted
                    ? "In Progress"
                    : "Not Accepted";
            ImGui.Text($"{quest.Name}  [{status}]");

            foreach (var prerequisite in questService.GetPrerequisites(quest.RowId))
            {
                var prerequisiteComplete = questService.GetStatus(prerequisite.RowId) == BeastmasterQuestStatus.Completed;
                ImGui.TextDisabled($"Prerequisite Quest: {prerequisite.Name}");
                ImGui.SameLine();
                ImGui.TextColored(
                    prerequisiteComplete
                        ? new Vector4(0.35f, 0.8f, 0.48f, 1f)
                        : new Vector4(0.9f, 0.32f, 0.3f, 1f),
                    prerequisiteComplete ? "[Completed]" : "[Incomplete]");
            }

            if (questService.TryGetIssuerLocation(quest.RowId, out var issuer))
            {
                ImGui.TextDisabled($"Start NPC: {issuer.NpcName} · {issuer.Zone}");
                if (completed)
                {
                    ImGui.BeginDisabled();
                }

                if (ImGui.Button($"Navigate to Start NPC##issuer-{quest.RowId}"))
                {
                    navigationService.Navigate(issuer);
                }

                if (completed)
                {
                    ImGui.EndDisabled();
                }
            }
            else
            {
                ImGui.TextDisabled("Start NPC: the client did not provide valid pickup coordinates");
            }

            ImGui.SameLine();
            BeastmasterQuestLocation? target = null;
            var hasTarget = questStatus == BeastmasterQuestStatus.Accepted
                && questService.TryGetCurrentTarget(quest.RowId, out target);
            if (!hasTarget)
            {
                ImGui.BeginDisabled();
            }

            if (ImGui.Button($"Navigate to Quest Objective##target-{quest.RowId}") && hasTarget)
            {
                navigationService.NavigateQuestTarget(target!);
            }

            if (!hasTarget)
            {
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                {
                    ImGui.SetTooltip("The target coordinates for the current quest step haven't been collected yet. ");
                }
            }

            ImGui.Separator();
        }

    }

    private static void DrawReserved(string title)
    {
        ImGui.Text(title);
        ImGui.Separator();
        ImGui.TextDisabled("This item is pending.");
    }

    private static void DrawCommands()
    {
        ImGui.Text("Quick Commands");
        ImGui.Separator();
        DrawGameCommandButton("Beast Catalog", "/beastcatalog");
        DrawGameCommandButton("Beastmaster Beast - Small", "/beastpetsize all small");
        DrawGameCommandButton("Beastmaster Beast - Medium", "/beastpetsize all medium");
        DrawGameCommandButton("Beastmaster Beast - Large", "/beastpetsize all large");
    }

    private void DrawEquipment()
    {
        RefreshEquipmentOwnership();
        ImGui.Text("Recommended Gear");
        ImGui.TextDisabled("Beastmaster level 50 starter and BIS gear sets.");
        var equipmentSet = selectedEquipmentSet;
        ImGui.SetNextItemWidth(220f);
        if (ImGui.Combo("Gear Plan", ref equipmentSet, "Starter Gear\0BIS\0"))
        {
            selectedEquipmentSet = equipmentSet;
        }
        ImGui.Separator();

        if (!ImGui.BeginTable(
                "BeastmasterEquipmentTable",
                4,
                ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp))
        {
            return;
        }

        ImGui.TableSetupColumn("Slot", ImGuiTableColumnFlags.WidthFixed, 90f);
        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Category", ImGuiTableColumnFlags.WidthFixed, 70f);
        ImGui.TableSetupColumn("Owned", ImGuiTableColumnFlags.WidthFixed, 210f);
        ImGui.TableHeadersRow();

        var entries = selectedEquipmentSet == 0
            ? BeastmasterEquipmentGuide.Level50Starter
            : BeastmasterEquipmentGuide.Level50BestInSlot;
        foreach (var equipment in entries)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(equipment.Slot);
            ImGui.TableNextColumn();
            if (equipment.Name == "No Gear")
            {
                ImGui.TextDisabled(equipment.Name);
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.45f, 0.75f, 1f, 1f));
                ImGui.Text(equipment.Name);
                ImGui.PopStyleColor();
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Click to view item details on the Wiki");
                }

                if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                {
                    OpenEquipmentWiki(equipment.Name);
                }
            }
            ImGui.TableNextColumn();
            ImGui.TextDisabled(equipment.Category);
            ImGui.TableNextColumn();
            DrawEquipmentOwnership(equipment);
        }

        ImGui.EndTable();
        ImGui.Spacing();
        ImGui.TextDisabled(selectedEquipmentSet == 0
            ? "Starter Gear: prioritizes ease of acquisition and getting geared quickly."
            : "BIS: prioritizes final level 50 combat stats. ");
    }

    private static void OpenEquipmentWiki(string equipmentName)
    {
        Process.Start(new ProcessStartInfo(
            $"https://ff14.huijiwiki.com/wiki/{Uri.EscapeDataString($"Item:{equipmentName}")}")
        {
            UseShellExecute = true,
        });
    }

    private void DrawEquipmentOwnership(BeastmasterEquipmentEntry equipment)
    {
        if (equipment.Name == "No Gear")
        {
            ImGui.TextDisabled("-");
            return;
        }

        if (!equipmentOwnership.TryGetValue(equipment.Name, out var ownership) || ownership.ItemId == 0)
        {
            ImGui.TextDisabled("ItemId Pending");
            return;
        }

        var equippedCount = ownership.EquippedCount;
        var inventoryCount = ownership.InventoryCount;
        var armoryCount = ownership.ArmoryCount;
        if (equippedCount > 0)
        {
            ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.5f, 1f), $"Equipped ({equippedCount})");
            ImGui.SameLine();
        }
        ImGui.TextColored(
            inventoryCount > 0 ? new Vector4(0.4f, 0.9f, 0.5f, 1f) : new Vector4(0.65f, 0.65f, 0.7f, 1f),
            inventoryCount > 0 ? $"In Inventory ({inventoryCount})" : "Not in Inventory");
        ImGui.SameLine();
        ImGui.TextColored(
            armoryCount > 0 ? new Vector4(0.4f, 0.8f, 1f, 1f) : new Vector4(0.65f, 0.65f, 0.7f, 1f),
            armoryCount > 0 ? $"In Armoury Chest ({armoryCount})" : "Not in Armoury Chest");
    }

    private void RefreshEquipmentOwnership()
    {
        if (DateTime.UtcNow < nextEquipmentRefreshUtc)
        {
            return;
        }

        equipmentOwnership.Clear();
        var selectedEntries = selectedEquipmentSet == 0
            ? BeastmasterEquipmentGuide.Level50Starter
            : BeastmasterEquipmentGuide.Level50BestInSlot;
        foreach (var equipment in selectedEntries.Where(item => item.Name != "No Gear"))
        {
            if (equipment.ItemId == 0)
            {
                equipmentOwnership[equipment.Name] = (0, 0, 0, 0);
                continue;
            }

            equipmentOwnership[equipment.Name] = (
                equipment.ItemId,
                CountItems(equipment.ItemId, [GameInventoryType.EquippedItems]),
                CountItems(equipment.ItemId, InventoryTypes()),
                CountItems(equipment.ItemId, ArmoryTypes(equipment.Slot)));
        }

        nextEquipmentRefreshUtc = DateTime.UtcNow.AddSeconds(1);
    }

    private static uint CountItems(uint itemId, IEnumerable<GameInventoryType> types)
    {
        var count = 0u;
        foreach (var type in types)
        {
            foreach (var inventoryItem in DalamudApi.GameInventory.GetInventoryItems(type))
            {
                if (!inventoryItem.IsEmpty && inventoryItem.BaseItemId == itemId)
                {
                    count += (uint)inventoryItem.Quantity;
                }
            }
        }

        return count;
    }

    private static IEnumerable<GameInventoryType> InventoryTypes()
    {
        yield return GameInventoryType.Inventory1;
        yield return GameInventoryType.Inventory2;
        yield return GameInventoryType.Inventory3;
        yield return GameInventoryType.Inventory4;
    }

    private static IEnumerable<GameInventoryType> ArmoryTypes(string slot)
    {
        if (slot == "Main Hand") yield return GameInventoryType.ArmoryMainHand;
        else if (slot == "Off Hand") yield return GameInventoryType.ArmoryOffHand;
        else if (slot == "Head") yield return GameInventoryType.ArmoryHead;
        else if (slot == "Body") yield return GameInventoryType.ArmoryBody;
        else if (slot == "Hands") yield return GameInventoryType.ArmoryHands;
        else if (slot == "Legs") yield return GameInventoryType.ArmoryLegs;
        else if (slot == "Feet") yield return GameInventoryType.ArmoryFeets;
        else if (slot == "Earrings") yield return GameInventoryType.ArmoryEar;
        else if (slot == "Necklace") yield return GameInventoryType.ArmoryNeck;
        else if (slot == "Bracelet") yield return GameInventoryType.ArmoryWrist;
        else if (slot.StartsWith("Ring", StringComparison.Ordinal)) yield return GameInventoryType.ArmoryRings;
        else if (slot == "Soul Crystal") yield return GameInventoryType.ArmorySoulCrystal;
    }

    private static void DrawGameCommandButton(string label, string command)
    {
        if (ImGui.Button(label) && !GameCommandService.Execute(command))
        {
            DalamudApi.ChatGui.Print($"[Beastmaster Assistant] Unable to execute {command}.");
        }
    }

    private void DrawCatalog()
    {
        ImGui.Text("Beast Catalog");
        ImGui.SameLine();
        if (ImGui.Button("WIKI"))
        {
            Process.Start(new ProcessStartInfo("https://ff14.huijiwiki.com/wiki/%E9%AD%94%E5%85%BD%E5%9B%BE%E9%89%B4")
            {
                UseShellExecute = true,
            });
        }

        ImGui.SameLine();
        var stopButtonWidth = ImGui.CalcTextSize("Stop Navigation").X + ImGui.GetStyle().FramePadding.X * 2f;
        var stopButtonX = ImGui.GetWindowContentRegionMax().X - stopButtonWidth;
        if (ImGui.GetCursorPosX() < stopButtonX)
        {
            ImGui.SetCursorPosX(stopButtonX);
        }

        if (ImGui.Button("Stop Navigation##catalog-stop-navigation"))
        {
            navigationService.Stop();
        }

        ImGui.TextDisabled("Sync Notes (?)");
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Automatically recorded on a successful capture; you can also manually edit completion status per character.\nResult Sync: automatically refreshes the beasts that fought after each round's results.\nNative Catalog: talk to Rowena to open the native beast catalog, then click "Sync Beast Level EXP" to automatically go through all beasts.\nLevel 25: max-level EXP shows as --/--, unsynced data shows as --.");
        }

        var entries = BeastmasterCatalog.Entries;
        var catalogCompletedCount = entries.Count(entry => progressService.IsCompleted(entry.Key));

        if (ImGui.Button("Sync Unlocked Beasts"))
        {
            catalogSyncService.RequestSync();
        }
        ImGui.SameLine();
        ImGui.TextDisabled($"{catalogCompletedCount}/{entries.Count}");
        if (catalogSyncService.IsScanning || !string.IsNullOrWhiteSpace(catalogSyncService.Diagnostic))
        {
            ImGui.SameLine();
            ImGui.TextDisabled(catalogSyncService.Status);
        }
        if (!string.IsNullOrWhiteSpace(catalogSyncService.Diagnostic))
        {
            ImGui.SameLine();
            if (ImGui.Button("Copy Catalog Diagnostics"))
            {
                ImGui.SetClipboardText(catalogSyncService.Diagnostic);
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Sync Beast Level EXP"))
        {
            notebookSyncService.RequestSync();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("First talk to Rowena to open the native beast catalog, then click sync.");
        }
        if (notebookSyncService.IsScanning)
        {
            ImGui.SameLine();
            ImGui.ProgressBar(
                (float)notebookSyncService.ProgressCount / notebookSyncService.TotalCount,
                new Vector2(120f, 0f),
                $"{notebookSyncService.ProgressCount}/{notebookSyncService.TotalCount}");
        }
        if (!string.IsNullOrWhiteSpace(notebookSyncService.Diagnostic))
        {
            ImGui.SameLine();
            ImGui.TextDisabled(notebookSyncService.Status);
            ImGui.SameLine();
            if (ImGui.Button("Copy Level Diagnostics"))
            {
                ImGui.SetClipboardText(notebookSyncService.Diagnostic);
            }
        }

        ImGui.Separator();

        var sortByLocation = configuration.SortCatalogByLocation;
        var hideCaptured = configuration.HideCapturedBeasts;
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.82f, 0.25f, 1f));
        if (ImGui.Checkbox("Sort by Map", ref sortByLocation))
        {
            configuration.SortCatalogByLocation = sortByLocation;
            configuration.Save();
        }
        ImGui.SameLine();
        var sortByLevel = configuration.SortCatalogByLevel;
        if (ImGui.Checkbox("Sort by Capture Level", ref sortByLevel))
        {
            configuration.SortCatalogByLevel = sortByLevel;
            if (sortByLevel) configuration.SortCatalogByBeastLevel = false;
            configuration.Save();
        }
        ImGui.SameLine();
        var sortByBeastLevel = configuration.SortCatalogByBeastLevel;
        if (ImGui.Checkbox("Sort by Beast Level", ref sortByBeastLevel))
        {
            configuration.SortCatalogByBeastLevel = sortByBeastLevel;
            if (sortByBeastLevel) configuration.SortCatalogByLevel = false;
            configuration.Save();
        }
        if (configuration.SortCatalogByBeastLevel)
        {
            ImGui.SameLine();
            var descending = configuration.SortCatalogByBeastLevelDescending;
            if (ImGui.Checkbox("Reverse Beast Level Order", ref descending))
            {
                configuration.SortCatalogByBeastLevelDescending = descending;
                configuration.Save();
            }
        }
        ImGui.SameLine();
        if (ImGui.Checkbox("Hide Captured Beasts", ref hideCaptured))
        {
            configuration.HideCapturedBeasts = hideCaptured;
            configuration.Save();
        }
        ImGui.PopStyleColor();

        var displayedEntries = entries.AsEnumerable();
        if (configuration.HideCapturedBeasts)
        {
            displayedEntries = displayedEntries.Where(e => !progressService.IsCompleted(e.Key));
        }

        var currentTerritory = DalamudApi.ClientState.TerritoryType;
        var currentMapRowId = DalamudApi.DataManager.GetExcelSheet<TerritoryType>()
            .TryGetRow(currentTerritory, out var currentTerritoryRow)
            ? currentTerritoryRow.Map.RowId
            : (ushort)0;

        ImGui.Spacing();

        if (!ImGui.BeginTable(
                "BeastmasterCatalogTable",
                10,
                ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp,
                new Vector2(0f, 0f)))
        {
            return;
        }

        ImGui.TableSetupColumn("Done", ImGuiTableColumnFlags.WidthFixed, 46f);
        ImGui.TableSetupColumn("No. / Beast", ImGuiTableColumnFlags.WidthFixed, 150f);
        ImGui.TableSetupColumn("Attribute", ImGuiTableColumnFlags.WidthFixed, 48f);
        ImGui.TableSetupColumn("Skill", ImGuiTableColumnFlags.WidthFixed, 118f);
        ImGui.TableSetupColumn("Capture Level", ImGuiTableColumnFlags.WidthFixed, 66f);
        ImGui.TableSetupColumn("Beast Level", ImGuiTableColumnFlags.WidthFixed, 42f);
        ImGui.TableSetupColumn("EXP", ImGuiTableColumnFlags.WidthFixed, 62f);
        ImGui.TableSetupColumn("Zone / Duty", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Coordinates", ImGuiTableColumnFlags.WidthFixed, 112f);
        ImGui.TableSetupColumn("Navigate", ImGuiTableColumnFlags.WidthFixed, 62f);
        ImGui.TableHeadersRow();

        var canEdit = progressService.CurrentCharacterKey.Length > 0;
        IEnumerable<BeastmasterCatalogEntry> sortedEntries;
        if (configuration.SortCatalogByLocation)
        {
            var ordered = displayedEntries
                .OrderBy(entry => !(entry.TerritoryType == currentTerritory
                    && (entry.MapRowId == 0 || entry.MapRowId == currentMapRowId)))
                .ThenBy(entry => entry.Location, StringComparer.Ordinal);
            sortedEntries = ApplyCatalogLevelSort(ordered);
        }
        else if (configuration.SortCatalogByBeastLevel)
        {
            sortedEntries = ApplyCatalogBeastLevelSort(displayedEntries);
        }
        else if (configuration.SortCatalogByLevel)
        {
            sortedEntries = displayedEntries
                .OrderBy(entry => GetCatalogMinimumLevel(entry.Level))
                .ThenBy(entry => entry.Number);
        }
        else
        {
            sortedEntries = displayedEntries.OrderBy(entry => entry.Number);
        }
        foreach (var entry in sortedEntries)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var completed = progressService.IsCompleted(entry.Key);
            if (!canEdit)
            {
                ImGui.BeginDisabled();
            }

            if (ImGui.Checkbox($"##catalog-complete-{entry.Number}", ref completed))
            {
                progressService.SetCompleted(entry.Key, completed);
            }

            if (!canEdit)
            {
                ImGui.EndDisabled();
            }

            ImGui.TableNextColumn();
            ImGui.Text($"{entry.Number}. {entry.Name}");
            ImGui.TableNextColumn();
            ImGui.TextColored(GetAttributeColor(entry.Attribute), entry.Attribute.ToString());
            ImGui.TableNextColumn();
            ImGui.TextWrapped($"{GetActionName(entry.UltimateActionId)} /\n{GetActionName(entry.ReleaseActionId)}");
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                DrawActionTooltip("Ultimate", entry.UltimateActionId);
                DrawActionTooltip("Release", entry.ReleaseActionId);
                ImGui.EndTooltip();
            }
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(entry.Level);
            ImGui.TableNextColumn();
            var beastProgress = progressService.GetBeastProgress(entry.Number);
            ImGui.TextUnformatted(beastProgress?.Level.ToString() ?? "--");
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(beastProgress == null
                ? "--/--"
                : beastProgress.Level >= 25
                    ? "--/--"
                    : $"{beastProgress.Experience}/{beastProgress.ExperienceRequired}");
            if (beastProgress != null && ImGui.IsItemHovered())
            {
                ImGui.SetTooltip($"Last Synced: {beastProgress.UpdatedUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
            }
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(entry.Location);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(GetCatalogCoordinate(entry));
            ImGui.TableNextColumn();
            if (entry.LocationType == BeastmasterCatalogLocationType.Field
                && entry.MapX.HasValue
                && entry.MapY.HasValue)
            {
                if (ImGui.SmallButton($"Navigate##catalog-nav-{entry.Number}"))
                {
                    navigationService.Navigate(entry);
                }
            }
            else if (entry.LocationType == BeastmasterCatalogLocationType.Duty
                && ImGui.SmallButton($"Duty##catalog-duty-{entry.Number}"))
            {
                navigationService.OpenDutyFinder(entry);
            }
        }

        ImGui.EndTable();
    }

    private IEnumerable<BeastmasterCatalogEntry> ApplyCatalogLevelSort(IOrderedEnumerable<BeastmasterCatalogEntry> ordered)
    {
        if (configuration.SortCatalogByBeastLevel)
        {
            var withKnownFirst = ordered.ThenBy(entry => progressService.GetBeastProgress(entry.Number) == null);
            return configuration.SortCatalogByBeastLevelDescending
                ? withKnownFirst.ThenByDescending(entry => progressService.GetBeastProgress(entry.Number)?.Level ?? 0)
                    .ThenBy(entry => entry.Number)
                : withKnownFirst.ThenBy(entry => progressService.GetBeastProgress(entry.Number)?.Level ?? int.MaxValue)
                    .ThenBy(entry => entry.Number);
        }

        return configuration.SortCatalogByLevel
            ? ordered.ThenBy(entry => GetCatalogMinimumLevel(entry.Level)).ThenBy(entry => entry.Number)
            : ordered.ThenBy(entry => entry.Number);
    }

    private IEnumerable<BeastmasterCatalogEntry> ApplyCatalogBeastLevelSort(IEnumerable<BeastmasterCatalogEntry> entries)
    {
        var knownFirst = entries.OrderBy(entry => progressService.GetBeastProgress(entry.Number) == null);
        return configuration.SortCatalogByBeastLevelDescending
            ? knownFirst.ThenByDescending(entry => progressService.GetBeastProgress(entry.Number)?.Level ?? 0)
                .ThenBy(entry => entry.Number)
            : knownFirst.ThenBy(entry => progressService.GetBeastProgress(entry.Number)?.Level ?? int.MaxValue)
                .ThenBy(entry => entry.Number);
    }

    private void DrawBeastArena()
    {
        ImGui.Text("Beast Arena Trials");
        ImGui.Separator();
        if (!ImGui.BeginTabBar("BeastArenaTabs"))
        {
            return;
        }

        DrawBeastArenaTab("party", "Trial Formation", DrawPartyPresets);
        DrawBeastArenaTab("achievements", "Beast Arena Achievements", DrawBeastArenaAchievements);
        DrawBeastArenaTab("guide", "Beast Arena Guide", DrawBeastArenaGuide);
        DrawBeastArenaTab("challenge-note", "Challenge Log", DrawBeastArenaChallengeNote);
        ImGui.EndTabBar();
        arenaTabSelectionInitialized = true;
        wasInAchievementsTab = configuration.SelectedArenaTab == "achievements";
    }

    private void DrawBeastArenaTab(string key, string label, System.Action draw)
    {
        var flags = !arenaTabSelectionInitialized && configuration.SelectedArenaTab == key
            ? ImGuiTabItemFlags.SetSelected
            : ImGuiTabItemFlags.None;
        if (!ImGui.BeginTabItem(label, flags))
        {
            return;
        }

        if (configuration.SelectedArenaTab != key)
        {
            configuration.SelectedArenaTab = key;
            configuration.Save();
        }

        draw();
        ImGui.EndTabItem();
    }

    private void DrawBeastArenaAchievements()
    {
        if (!wasInAchievementsTab)
        {
            achievementSyncService.RequestSync();
        }

        ImGui.Spacing();
        ImGui.Text("Beast Arena Achievements");
        ImGui.SameLine();
        ImGui.TextDisabled("Saved per character; opening this tab automatically syncs completion status.");
        ImGui.Spacing();

        if (!string.IsNullOrWhiteSpace(achievementSyncService.Diagnostic))
        {
            if (ImGui.Button("Copy Sync Diagnostics"))
            {
                ImGui.SetClipboardText(achievementSyncService.Diagnostic);
            }

            ImGui.SameLine();
        }

        ImGui.TextDisabled(achievementSyncService.Status);
        ImGui.Separator();

        var achievementSheet = DalamudApi.DataManager.GetExcelSheet<Achievement>();
        var total = BeastmasterAchievementCatalog.AchievementCount;
        var completedCount = 0;
        foreach (var group in BeastmasterAchievementCatalog.Groups)
        {
            foreach (var achievementId in group.AchievementIds)
            {
                if (progressService.IsAchievementCompleted(achievementId))
                {
                    completedCount++;
                }
            }
        }

        ImGui.ProgressBar((float)completedCount / total, new Vector2(-1f, 0f), $"{completedCount}/{total}");

        ImGui.Spacing();
        var hideCompletedAchievements = configuration.HideCompletedAchievements;
        if (ImGui.Checkbox("Hide Completed", ref hideCompletedAchievements))
        {
            configuration.HideCompletedAchievements = hideCompletedAchievements;
            configuration.Save();
        }

        ImGui.Spacing();
        ImGui.TextColored(new Vector4(1f, 0.82f, 0.25f, 1f), "Legendary Beastmaster Score Threshold");
        ImGui.TextDisabled(string.Join(" · ", BeastmasterAchievementCatalog.LegendaryPoints.Select(point => $"{point.Arena} {point.Points}")));
        ImGui.Spacing();

        foreach (var group in BeastmasterAchievementCatalog.Groups)
        {
            ImGui.TextColored(new Vector4(1f, 0.82f, 0.25f, 1f), group.Name);
            ImGui.Separator();

            foreach (var achievementId in group.AchievementIds)
            {
                if (configuration.HideCompletedAchievements
                    && progressService.IsAchievementCompleted(achievementId))
                {
                    continue;
                }

                if (!achievementSheet.TryGetRow((uint)achievementId, out var achievement))
                {
                    ImGui.TextDisabled($"Achievement data not found for #{achievementId}");
                    continue;
                }

                var name = achievement.Name.ExtractText();
                var description = achievement.Description.ExtractText();
                var points = achievement.Points;
                var titleText = achievement.Title.Value.Masculine.ExtractText();
                if (string.IsNullOrWhiteSpace(titleText))
                {
                    titleText = achievement.Title.Value.Feminine.ExtractText();
                }

                var isCompleted = progressService.IsAchievementCompleted(achievementId);
                var stateColor = isCompleted
                    ? new Vector4(0.35f, 0.8f, 0.48f, 1f)
                    : new Vector4(0.62f, 0.62f, 0.62f, 1f);

                ImGui.TextColored(stateColor, $"{achievementId} {name}");
                ImGui.SameLine();
                ImGui.TextDisabled($"[{points}]");
                ImGui.SameLine();
                ImGui.TextColored(stateColor, isCompleted ? "[Completed]" : "[Incomplete]");

                if (!string.IsNullOrWhiteSpace(description))
                {
                    ImGui.TextDisabled($"  {description}");
                }

                if (!string.IsNullOrWhiteSpace(titleText))
                {
                    ImGui.TextDisabled($"  Title: {titleText}");
                }
            }

            ImGui.Spacing();
        }
    }

    private static void DrawBeastArenaChallengeNote()
    {
        ImGui.Spacing();
        ImGui.Text("Challenge Log");
        ImGui.SameLine();
        ImGui.TextDisabled("Syncs completion status for "Beast Arena Trials" related challenges when opened.");
        ImGui.Spacing();

        var entries = BeastmasterChallengeNote.GetBeastArenaEntries();
        if (entries.Count == 0)
        {
            ImGui.TextDisabled("No "Beast Arena Trials" related Challenge Log entries found.");
            return;
        }

        if (!BeastmasterChallengeNote.IsLoaded())
        {
            ImGui.TextColored(new Vector4(1f, 0.6f, 0.3f, 1f), "Challenge Log data hasn't loaded yet; please open the Challenge Log in-game first.");
            return;
        }

        var completedCount = entries.Count(entry => BeastmasterChallengeNote.IsComplete(entry.RowId));
        ImGui.ProgressBar((float)completedCount / entries.Count, new Vector2(-1f, 0f), $"{completedCount}/{entries.Count}");
        ImGui.Spacing();

        foreach (var entry in entries)
        {
            var isCompleted = BeastmasterChallengeNote.IsComplete(entry.RowId);
            var color = isCompleted
                ? new Vector4(0.35f, 0.8f, 0.48f, 1f)
                : new Vector4(0.9f, 0.32f, 0.3f, 1f);

            ImGui.TextColored(color, isCompleted ? "[Completed]" : "[Incomplete]");
            ImGui.SameLine();
            ImGui.TextColored(color, entry.Name);
            if (!string.IsNullOrWhiteSpace(entry.Description) && ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(entry.Description);
            }
        }
    }

    private static void DrawBeastArenaGuide()
    {
        ImGui.Spacing();
        ImGui.Text("Beast Arena Guide");
        ImGui.Separator();

        if (!ImGui.BeginTabBar("BeastArenaGuideTabs"))
        {
            return;
        }

        DrawGuideFloor("Round 1", null);
        DrawGuideFloor("Round 2", null);
        DrawGuideFloor("Round 3", BeastmasterArenaGuide.Round3, BeastmasterArenaGuide.Round3Author);
        DrawGuideFloor("Advanced Round 1", null);
        DrawGuideFloor("Advanced Round 2", null);

        ImGui.EndTabBar();
    }

    private static void DrawGuideFloor(string label, IReadOnlyList<BeastmasterArenaGuideRound>? rounds, string? author = null)
    {
        if (!ImGui.BeginTabItem(label))
        {
            return;
        }

        if (rounds == null || rounds.Count == 0)
        {
            ImGui.TextDisabled("Guide data for this floor is pending.");
        }
        else
        {
            ImGui.TextDisabled("Yellow marks the boss, gray marks minions, and red marks key skills.");
            if (!string.IsNullOrWhiteSpace(author))
            {
                ImGui.SameLine();
                ImGui.TextDisabled($"Author: {author}");
            }
            ImGui.Spacing();
            foreach (var round in rounds)
            {
                ImGui.TextColored(new Vector4(1f, 0.6f, 0.3f, 1f), round.Position);

                var bosses = string.Join("、", round.Monsters.Where(monster => monster.IsBoss).Select(monster => monster.Name));
                if (bosses.Length > 0)
                {
                    DrawWrappedColoredText($"BOSS：{bosses}", new Vector4(1f, 0.82f, 0.25f, 1f));
                }

                var minions = string.Join("、", round.Monsters.Where(monster => !monster.IsBoss).Select(monster => monster.Name));
                if (minions.Length > 0)
                {
                    DrawWrappedColoredText($"Minions: {minions}", new Vector4(0.58f, 0.62f, 0.7f, 1f));
                }

                var highlights = ExtractGuideHighlights(round.Mechanic);
                if (highlights.Count > 0)
                {
                    DrawWrappedColoredText($"Key Points: {string.Join("、", highlights)}", new Vector4(1f, 0.45f, 0.4f, 1f));
                }

                if (!string.IsNullOrWhiteSpace(round.Mechanic))
                {
                    ImGui.TextWrapped($"Mechanic: {round.Mechanic}");
                }

                if (!string.IsNullOrWhiteSpace(round.Comment))
                {
                    DrawWrappedColoredText($"Psst: {round.Comment}", new Vector4(0.58f, 0.62f, 0.7f, 1f));
                }

                ImGui.Separator();
                ImGui.Spacing();
            }
        }

        ImGui.EndTabItem();
    }

    private static void DrawWrappedColoredText(string text, Vector4 color)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
    }

    private static IReadOnlyList<string> ExtractGuideHighlights(string text)
    {
        var highlights = new List<string>();
        var searchIndex = 0;
        while (searchIndex < text.Length)
        {
            var start = text.IndexOf('「', searchIndex);
            if (start < 0)
            {
                break;
            }

            var end = text.IndexOf('」', start + 1);
            if (end < 0)
            {
                break;
            }

            if (end > start + 1)
            {
                highlights.Add(text[(start + 1)..end]);
            }

            searchIndex = end + 1;
        }

        return highlights;
    }

    private void DrawPartyPresets()
    {
        ImGui.Spacing();
        ImGui.TextColored(new Vector4(1f, 0.3f, 0.25f, 1f),
            "Preset slots can be 10/12/14/15; when applied, they're trimmed or left with empty slots based on your current in-game formation size.");
        ImGui.Separator();

        var presets = configuration.PartyPresets;
        if (presets.Count == 0)
        {
            presets.Add(new BeastmasterPartyPreset());
            configuration.SelectedPartyPresetIndex = 0;
            configuration.Save();
        }

        var selectedIndex = Math.Clamp(configuration.SelectedPartyPresetIndex, 0, presets.Count - 1);
        if (ImGui.Button("New"))
        {
            presets.Add(new BeastmasterPartyPreset { Name = GetUniquePartyPresetName(presets, "10-Slot Formation") });
            configuration.SelectedPartyPresetIndex = presets.Count - 1;
            partyPresetStatus = "New formation preset created.";
            configuration.Save();
        }
        ImGui.SameLine();
        if (ImGui.Button("Duplicate"))
        {
            var copy = presets[selectedIndex].Clone();
            copy.Name = GetUniquePartyPresetName(presets, copy.Name);
            presets.Add(copy);
            configuration.SelectedPartyPresetIndex = presets.Count - 1;
            partyPresetStatus = "Current preset duplicated.";
            configuration.Save();
        }
        ImGui.SameLine();
        ImGui.BeginDisabled(presets.Count <= 1);
        if (ImGui.Button("Delete"))
        {
            presets.RemoveAt(selectedIndex);
            configuration.SelectedPartyPresetIndex = Math.Clamp(selectedIndex, 0, presets.Count - 1);
            partyPresetStatus = "Current preset deleted.";
            configuration.Save();
        }
        ImGui.EndDisabled();
        ImGui.SameLine(0f, 18f);
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.61f, 0.34f, 0.21f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.73f, 0.44f, 0.28f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.52f, 0.27f, 0.17f, 1f));
        if (ImGui.Button("Share"))
        {
            ImGui.SetClipboardText(presets[selectedIndex].Export());
            partyPresetStatus = "Current formation preset copied to clipboard.";
        }
        ImGui.PopStyleColor(3);
        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.55f, 0.31f, 0.34f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.67f, 0.41f, 0.44f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.47f, 0.25f, 0.28f, 1f));
        if (ImGui.Button("Import"))
        {
            if (BeastmasterPartyPreset.TryImport(ImGui.GetClipboardText(), out var imported, out var error)
                && imported != null)
            {
                imported.Name = GetUniquePartyPresetName(presets, imported.Name);
                presets.Add(imported);
                configuration.SelectedPartyPresetIndex = presets.Count - 1;
                partyPresetStatus = "Formation preset imported from clipboard.";
                configuration.Save();
            }
            else
            {
                partyPresetStatus = $"Import failed: {error}";
            }
        }
        ImGui.PopStyleColor(3);
        ImGui.SameLine(0f, 18f);
        selectedIndex = Math.Clamp(configuration.SelectedPartyPresetIndex, 0, presets.Count - 1);
        var selectedPreset = presets[selectedIndex];
        ImGui.BeginDisabled(!petPartyService.Snapshot.Available || petPartyService.IsApplying || !CanApplyPartyPreset(selectedPreset));
        PushPartyApplyButtonStyle();
        if (ImGui.Button(petPartyService.IsApplying ? "Applying..." : "Apply Formation", new Vector2(110f, 0f)))
        {
            petPartyService.TryApply(selectedPreset);
        }
        ImGui.PopStyleColor(3);
        ImGui.EndDisabled();

        var presetNames = string.Join('\0', presets.Select(item => item.Name)) + '\0';
        selectedIndex = Math.Clamp(configuration.SelectedPartyPresetIndex, 0, presets.Count - 1);
        ImGui.SetNextItemWidth(300f);
        if (ImGui.Combo("Formation Presets", ref selectedIndex, presetNames))
        {
            configuration.SelectedPartyPresetIndex = selectedIndex;
            configuration.Save();
        }

        selectedIndex = Math.Clamp(configuration.SelectedPartyPresetIndex, 0, presets.Count - 1);
        var preset = presets[selectedIndex];
        var name = preset.Name;
        ImGui.SetNextItemWidth(135f);
        if (ImGui.InputText("Preset Name", ref name, 100) && !string.IsNullOrWhiteSpace(name))
        {
            preset.Name = name.Trim();
            configuration.Save();
        }

        ImGui.SameLine();
        var slotCount = preset.SlotCount;
        var slotIndex = slotCount switch { 12 => 1, 14 => 2, 15 => 3, _ => 0 };
        ImGui.SetNextItemWidth(100f);
        if (ImGui.Combo("Preset Slots", ref slotIndex, "10\0 12\0 14\0 15\0"))
        {
            preset.SlotCount = slotIndex switch { 1 => 12, 2 => 14, 3 => 15, _ => 10 };
            if (preset.Members.Count > preset.SlotCount)
            {
                preset.Members.RemoveRange(preset.SlotCount, preset.Members.Count - preset.SlotCount);
            }
            configuration.Save();
        }

        ImGui.Text($"Preset Members: {preset.Members.Count}/{preset.SlotCount}");
        if (petPartyService.Snapshot.Available && preset.SlotCount != petPartyService.Snapshot.Capacity)
        {
            var difference = petPartyService.Snapshot.Capacity - preset.SlotCount;
            ImGui.TextColored(new Vector4(1f, 0.75f, 0.25f, 1f), difference < 0
                ? $"Your current formation only has {petPartyService.Snapshot.Capacity} slots, so only the first {petPartyService.Snapshot.Capacity} positions of the preset will be applied."
                : $"Your current formation has {petPartyService.Snapshot.Capacity} slots; {difference} of them will be left empty after applying.");
        }
        var availableBeasts = BeastmasterCatalog.Entries
            .Where(entry => progressService.IsCompleted(entry.Key))
            .ToArray();
        for (var position = 0; position < preset.SlotCount; position++)
        {
            var currentNumber = position < preset.Members.Count ? preset.Members[position] : 0;
            var options = new List<(int Number, string Label)> { (0, "-- Empty --") };
            options.AddRange(availableBeasts.Select(entry =>
            {
                var progress = progressService.GetBeastProgress(entry.Number);
                var level = progress == null ? "Lv.--" : $"Lv.{progress.Level}";
                return (entry.Number, $"{entry.Number:00} - {entry.Name} - {level}");
            }));

            var optionIndex = options.FindIndex(option => option.Number == currentNumber);
            if (optionIndex < 0) optionIndex = 0;
            ImGui.SetNextItemWidth(330f);
            if (ImGui.Combo($"Slot {position + 1:00}##party-member-{position}", ref optionIndex,
                    string.Join('\0', options.Select(option => option.Label)) + '\0'))
            {
                SetPartyPresetMember(preset, position, options[optionIndex].Number);
            }
        }

        if (!preset.TryValidate(out var validationError))
        {
            ImGui.TextColored(new Vector4(1f, 0.4f, 0.3f, 1f), validationError);
        }
        else
        {
            var unavailableMembers = preset.Members
                .Where(number => !progressService.IsCompleted(BeastmasterCatalog.Entries[number - 1].Key))
                .ToArray();
            if (unavailableMembers.Length > 0)
            {
                ImGui.TextColored(new Vector4(1f, 0.4f, 0.3f, 1f),
                    $"Not captured or unavailable: {string.Join(", ", unavailableMembers.Select(number => number.ToString("00")))}");
            }
        }

        if (petPartyService.Snapshot.Available)
        {
            ImGui.Separator();
            DrawCurrentPetParty(preset);
        }
        if (!string.IsNullOrWhiteSpace(partyPresetStatus))
        {
            ImGui.TextWrapped(partyPresetStatus);
        }

    }

    private void DrawCurrentPetParty(BeastmasterPartyPreset preset)
    {
        var snapshot = petPartyService.Snapshot;
        ImGui.Text("Current In-Game Formation");
        if (!snapshot.Available)
        {
            ImGui.TextDisabled(snapshot.Reason);
            return;
        }

        ImGui.TextDisabled($"Current Formation: {snapshot.MemberCount}/{snapshot.Capacity}");
        var maximum = Math.Max(snapshot.Members.Count, preset.Members.Count);
        for (var index = 0; index < maximum; index++)
        {
            var current = index < snapshot.Members.Count ? snapshot.Members[index].CatalogNumber : 0;
            var expected = index < preset.Members.Count ? preset.Members[index] : 0;
            var currentText = current == 0 ? "Empty" : $"{current:00} {BeastmasterCatalog.Entries[current - 1].Name}";
            var expectedText = expected == 0 ? "Empty" : $"{expected:00} {BeastmasterCatalog.Entries[expected - 1].Name}";
            var matches = current == expected;
            ImGui.TextColored(matches
                    ? new Vector4(0.45f, 0.8f, 0.5f, 1f)
                    : new Vector4(1f, 0.65f, 0.25f, 1f),
                $"{index + 1:00}: {currentText} → {expectedText}");
        }

    }

    private void SetPartyPresetMember(BeastmasterPartyPreset preset, int position, int number)
    {
        if (number == 0)
        {
            if (position < preset.Members.Count)
            {
                preset.Members.RemoveRange(position, preset.Members.Count - position);
            }
            partyPresetStatus = "Cleared this slot and all following members.";
            configuration.Save();
            return;
        }

        if (preset.Members.Contains(number))
        {
            partyPresetStatus = $"Catalog #{number:00} is already in this preset and can't be added again.";
            return;
        }

        if (position < preset.Members.Count)
        {
            preset.Members[position] = number;
        }
        else if (position == preset.Members.Count)
        {
            preset.Members.Add(number);
        }
        else
        {
            partyPresetStatus = "Please fill in the previous slot first — empty slots can only be at the end of the list.";
            return;
        }

        partyPresetStatus = "Preset saved.";
        configuration.Save();
    }

    private static string GetUniquePartyPresetName(IEnumerable<BeastmasterPartyPreset> presets, string baseName)
    {
        var names = presets.Select(preset => preset.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!names.Contains(baseName)) return baseName;
        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{baseName} ({suffix})";
            if (!names.Contains(candidate)) return candidate;
        }
    }

    private bool CanApplyPartyPreset(BeastmasterPartyPreset preset)
        => preset.TryValidate(out _)
            && preset.Members.All(number => progressService.IsCompleted(BeastmasterCatalog.Entries[number - 1].Key));

    private static string GetCatalogCoordinate(BeastmasterCatalogEntry entry)
        => entry.LocationType switch
        {
            BeastmasterCatalogLocationType.Field when entry.MapX.HasValue && entry.MapY.HasValue
                => $"X:{entry.MapX:0.#}, Y:{entry.MapY:0.#}",
            BeastmasterCatalogLocationType.Duty => "Duty",
            BeastmasterCatalogLocationType.Starting => "Default",
            _ => "Unknown",
        };

    private static int GetCatalogMinimumLevel(string level)
    {
        var digits = new string(level.TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var value) ? value : int.MaxValue;
    }

    private static Vector4 GetAttributeColor(BeastmasterAttribute attribute)
        => attribute switch
        {
            BeastmasterAttribute.Ferocity => new Vector4(0.95f, 0.35f, 0.3f, 1f),
            BeastmasterAttribute.Fortitude => new Vector4(0.35f, 0.65f, 1f, 1f),
            BeastmasterAttribute.Magic => new Vector4(1f, 0.82f, 0.25f, 1f),
            BeastmasterAttribute.Flight => new Vector4(0.4f, 0.9f, 0.5f, 1f),
            _ => new Vector4(0.6f, 0.6f, 0.6f, 1f),
        };

    private static string GetActionName(uint actionId)
        => DalamudApi.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>().TryGetRow(actionId, out var action)
            ? action.Name.ExtractText()
            : actionId.ToString();

    private static void DrawActionTooltip(string type, uint actionId)
    {
        var actions = DalamudApi.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>();
        if (!actions.TryGetRow(actionId, out var action))
        {
            ImGui.TextDisabled($"{type}: ActionId {actionId} not found");
            return;
        }

        ImGui.Text($"{type}：{action.Name.ExtractText()}");
        ImGui.TextDisabled($"ActionId: {actionId} · Level: {action.ClassJobLevel} · Range: {action.Range} · Radius: {action.EffectRange}");
    }

    private void DrawSettings()
    {
        ImGui.Text("Settings");
        ImGui.Separator();

        ImGui.Text("Dependency Plugins");
        DrawDependency("vnavmesh", navigationService.IsVnavmeshInstalled, "Same-map pathfinding and movement");
        DrawDependency("Lifestream", navigationService.IsLifestreamInstalled, "Cross-map teleport");
        ImGui.Spacing();

        ImGui.Text("General Settings");
        ImGui.TextDisabled($"Current Character: {progressService.CurrentCharacterLabel}");
        DrawSettingCheckbox("Hide Completed Quests", "The Quests tab only shows incomplete Beastmaster quests.", nameof(configuration.HideCompletedQuests), configuration.HideCompletedQuests);
        DrawSettingCheckbox("Auto-Record on Capture Message", "Automatically marks the catalog entry as complete when you receive a successful capture message.", nameof(configuration.AutoCompleteCatalogFromChat), configuration.AutoCompleteCatalogFromChat);
        ImGui.Spacing();

        ImGui.Text("Navigation Settings");
        DrawSettingCheckbox("Flight Navigation", "Allows vnavmesh to use flight paths.", nameof(configuration.UseFlightNavigation), configuration.UseFlightNavigation);
        DrawSettingCheckbox("Set Map Marker", "Also sets the in-game map flag when you click navigate.", nameof(configuration.SetFlagOnNavigation), configuration.SetFlagOnNavigation);
        DrawSettingCheckbox("Show Navigation Log", "Shows navigation start and failure messages in the chat log.", nameof(configuration.ShowNavigationLogs), configuration.ShowNavigationLogs);
    }

    private void DrawAutoOutput()
    {
        ImGui.Text("Auto Rotation");
        ImGui.TextDisabled("Configure auto-capture and auto-attack behavior.");
        ImGui.Separator();

        var enabled = autoCaptureService.IsEnabled;
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.82f, 0.25f, 1f));
        if (ImGui.Checkbox("Auto Rotation", ref enabled))
        {
            autoCaptureService.SetEnabled(enabled);
        }
        ImGui.PopStyleColor();
        var paused = autoCaptureService.IsPaused;
        if (ImGui.Checkbox("Pause", ref paused))
        {
            autoCaptureService.SetPaused(paused);
        }
        ImGui.SameLine();
        ImGui.TextDisabled("While paused, the Auto Rotation master switch stays on, but no actions are executed");
        ImGui.TextDisabled("Only applies to your currently manually-selected enemy target; if the capture buff is missing, Capture takes priority, then the 1→2→3 combo is used.");
        var activeAttack = autoCaptureService.ActiveAttack;
        if (ImGui.Checkbox("Active Attack", ref activeAttack))
        {
            autoCaptureService.SetActiveAttack(activeAttack);
        }
        ImGui.SameLine();
        ImGui.TextDisabled("When off, won't attack, capture, or use Beast Arena skills while not in combat");
        var forceCapture = autoCaptureService.ForceCapture;
        if (ImGui.Checkbox("Force Capture", ref forceCapture))
        {
            autoCaptureService.SetForceCapture(forceCapture);
        }
        ImGui.SameLine();
        ImGui.TextDisabled("When Try Capture is on, ignores the target's capture buff but still respects the HP threshold");
        DrawCaptureHpThreshold();
        DrawSettingCheckbox(
            "Verbose Mode",
            "Shows extra details in the overlay: reasons, capture state, current beast, gauges, and advanced skill candidates. Off by default.",
            nameof(configuration.ShowGaugeInOverlay),
            configuration.ShowGaugeInOverlay);
        DrawAutoOutputDiagnosticsSettings();
        DrawSettingCheckbox(
            "3-Column Overlay Layout",
            "Arranges the overlay's advanced skill buttons into three columns. Off by default.",
            nameof(configuration.OverlayThreeColumnMode),
            configuration.OverlayThreeColumnMode);
        ImGui.Spacing();
        DrawAdvancedActionToggles();
        ImGui.TextDisabled("Priority: Skill Sequence → Recovery Item → Rule Mode → Link (2nd Hit) → Release → Finishing Blow → Rally → Cheer → Convergence → Link (1st Hit) → Safety Shield → Capture → Basic Combo");

        ImGui.Spacing();
        DrawSequenceSettings();

        ImGui.Spacing();
        ImGui.Text("Current Mode");
        ImGui.Text(autoCaptureService.IsEnabled
            ? autoCaptureService.IsPaused
                ? "Paused"
                : autoCaptureService.ForceCapture
                    ? "Force Capturing..."
                    : autoCaptureService.TryCapture ? "Auto Capturing..." : "Auto Attacking..."
            : "Off");
        ImGui.TextDisabled("Once enabled, you can toggle "Try Capture" from the overlay; right-click the overlay to open Settings.");

        ImGui.Spacing();
        DrawBeastmasterGauge();

    }

    private void DrawAutoOutputDiagnosticsSettings()
    {
        if (!ImGui.CollapsingHeader("Auto Rotation Diagnostics##AutoOutputDiagnostics"))
        {
            return;
        }

        ImGui.Indent();
        DrawSettingCheckbox(
            "Enable Auto Rotation Diagnostics",
            "Logs a summary whenever any diagnostic module's status or reason changes, and records it to the Combat Log.",
            nameof(configuration.AutoOutputDiagnosticsEnabled),
            configuration.AutoOutputDiagnosticsEnabled);
        ImGui.Spacing();
        if (ImGui.Button("Copy Selected Combat Log"))
        {
            ImGui.SetClipboardText(autoCaptureService.GetBattleLog(selectedBattleLogIndex));
        }
        ImGui.SameLine();
        if (ImGui.Button("Clear Combat Log")) autoCaptureService.ClearBattleLogs();
        var battleLogs = autoCaptureService.BattleLogLabels;
        if (battleLogs.Count > 0)
        {
            selectedBattleLogIndex = Math.Clamp(selectedBattleLogIndex, 0, battleLogs.Count - 1);
            var labels = string.Join('\0', battleLogs) + '\0';
            ImGui.SetNextItemWidth(260f);
            ImGui.Combo("Combat Log", ref selectedBattleLogIndex, labels);
        }
        else
        {
            selectedBattleLogIndex = 0;
            ImGui.TextDisabled("No combat log yet");
        }
        ImGui.Unindent();
    }

    private void DrawAdvancedActionToggles(bool compactFinalStrike = false)
    {
        if (!compactFinalStrike && !ImGui.CollapsingHeader("Advanced Skills##BeastmasterAdvancedActions"))
        {
            return;
        }

        if (!compactFinalStrike) ImGui.Indent();
        if (compactFinalStrike)
        {
            if (configuration.OverlayThreeColumnMode)
            {
                var threeColumn = 0;
                DrawOverlayAdvancedToggle("Taming Link", configuration.BeastHeartCooperationEnabled, () => ToggleCooperation(true), "Taming Link (Yellow Orb)", ref threeColumn, columnCount: 3);
                DrawOverlayAdvancedToggle("Beast Spirit Link", configuration.BeastSoulCooperationEnabled, () => ToggleCooperation(false), "Beast Spirit Link (Blue Orb)", ref threeColumn, columnCount: 3);
                DrawOverlayAdvancedToggle("Rally", configuration.AutoDrumEnabled, () => ToggleBoolean(nameof(configuration.AutoDrumEnabled)), "Rally · Auto-Use When Ready", ref threeColumn, columnCount: 3);
                DrawOverlayAdvancedToggle("Convergence · Physical", configuration.PhysicalThirdFormEnabled, () => ToggleThirdForm(true), "Convergence (Physical)", ref threeColumn, columnCount: 3);
                DrawOverlayAdvancedToggle("Convergence · Magic", configuration.MagicalThirdFormEnabled, () => ToggleThirdForm(false), "Convergence (Magic)", ref threeColumn, columnCount: 3);
                DrawOverlayAdvancedToggle("Cheer", configuration.AutoCheerEnabled, () => ToggleBoolean(nameof(configuration.AutoCheerEnabled)), "Cheer · Auto-Use When Ready", ref threeColumn, columnCount: 3);
                DrawOverlayAdvancedToggle("Auto Beast Whistle", configuration.AutoWhistleEnabled, () => ToggleBoolean(nameof(configuration.AutoWhistleEnabled)), "Auto Beast Whistle", ref threeColumn, columnCount: 3);
                DrawOverlayAdvancedToggle("Finishing Blow", configuration.AutoFinalStrikeEnabled, () => autoCaptureService.SetFinalStrikeEnabled(!configuration.AutoFinalStrikeEnabled), "Finishing Blow", ref threeColumn, columnCount: 3);
                DrawOverlayAdvancedToggle("Release", configuration.AutoReleaseEnabled, () => ToggleBoolean(nameof(configuration.AutoReleaseEnabled)), "Release · Auto-Use When Ready", ref threeColumn, columnCount: 3);
                DrawOverlayAdvancedToggle("Sustained Provoke", IsArenaRuleEnabled(46751, 2413), () => ToggleArenaRule(46751, 2413), "Sustained Provoke", ref threeColumn, columnCount: 3, yellowWhenEnabled: true);
                DrawOverlayAdvancedToggle("Sustained Taunt", IsArenaRuleEnabled(46750, 5586), () => ToggleArenaRule(46750, 5586), "Sustained Taunt", ref threeColumn, columnCount: 3, yellowWhenEnabled: true);
                DrawOverlayAdvancedToggle("Safety Shield", configuration.AutoSafeShieldEnabled, () => ToggleBoolean(nameof(configuration.AutoSafeShieldEnabled)), "Safety Shield: automatically used when you're within 3 yalms of the target and Shield Bash is available.", ref threeColumn, columnCount: 3, yellowWhenEnabled: true);
                return;
            }

            var column = 0;
            DrawOverlayAdvancedToggle("Taming Link", configuration.BeastHeartCooperationEnabled,
                () =>
                {
                    configuration.BeastHeartCooperationEnabled = !configuration.BeastHeartCooperationEnabled;
                    if (configuration.BeastHeartCooperationEnabled) configuration.BeastSoulCooperationEnabled = false;
                    configuration.Save();
                }, "Taming Link (Yellow Orb)", ref column);
            DrawOverlayAdvancedToggle("Beast Spirit Link", configuration.BeastSoulCooperationEnabled,
                () =>
                {
                    configuration.BeastSoulCooperationEnabled = !configuration.BeastSoulCooperationEnabled;
                    if (configuration.BeastSoulCooperationEnabled) configuration.BeastHeartCooperationEnabled = false;
                    configuration.Save();
                }, "Beast Spirit Link (Blue Orb)", ref column);
            DrawOverlayAdvancedToggle("Convergence · Physical", configuration.PhysicalThirdFormEnabled,
                () =>
                {
                    configuration.PhysicalThirdFormEnabled = !configuration.PhysicalThirdFormEnabled;
                    if (configuration.PhysicalThirdFormEnabled) configuration.MagicalThirdFormEnabled = false;
                    configuration.Save();
                }, "Convergence (Physical)", ref column);
            DrawOverlayAdvancedToggle("Convergence · Magic", configuration.MagicalThirdFormEnabled,
                () =>
                {
                    configuration.MagicalThirdFormEnabled = !configuration.MagicalThirdFormEnabled;
                    if (configuration.MagicalThirdFormEnabled) configuration.PhysicalThirdFormEnabled = false;
                    configuration.Save();
                }, "Convergence (Magic)", ref column);
            DrawOverlayAdvancedToggle("Rally", configuration.AutoDrumEnabled,
                () =>
                {
                    configuration.AutoDrumEnabled = !configuration.AutoDrumEnabled;
                    configuration.Save();
                }, "Rally · Auto-Use When Ready: evaluated normally when Heart of Taming is 0; when Heart of Taming is above 0, it's only evaluated if Convergence (Physical or Magic) is enabled. Automatically uses Rally (44905) whenever the game allows it.", ref column);
            DrawOverlayAdvancedToggle("Cheer", configuration.AutoCheerEnabled,
                () =>
                {
                    configuration.AutoCheerEnabled = !configuration.AutoCheerEnabled;
                    configuration.Save();
                }, "Cheer · Auto-Use When Ready: evaluated normally when Heart of the Beast Spirit is 0; when it's above 0, it's only evaluated if Convergence (Physical or Magic) is enabled. Automatically uses Cheer (44904) whenever the game allows it.", ref column);
            DrawOverlayAdvancedToggle("Auto Beast Whistle", configuration.AutoWhistleEnabled,
                () =>
                {
                    configuration.AutoWhistleEnabled = !configuration.AutoWhistleEnabled;
                    configuration.Save();
                }, "When no beast is out, cycles Whistle 1→2→3 to use the first available one; waits 1 second after requesting a summon to confirm before trying the next whistle, avoiding accidental double-uses.", ref column);
            DrawOverlayAdvancedToggle("Safety Shield", configuration.AutoSafeShieldEnabled, () => ToggleBoolean(nameof(configuration.AutoSafeShieldEnabled)), "Safety Shield: automatically used when you're within 3 yalms of the target and Shield Bash is available.", ref column, yellowWhenEnabled: true);
            DrawOverlayAdvancedToggle("Release", configuration.AutoReleaseEnabled,
                () =>
                {
                    configuration.AutoReleaseEnabled = !configuration.AutoReleaseEnabled;
                    configuration.Save();
                }, "Release · Auto-Use When Ready: automatically uses Release when the game allows it and the summoned beast is within Release range.", ref column);
            DrawOverlayAdvancedToggle("Finishing Blow", configuration.AutoFinalStrikeEnabled,
                () => autoCaptureService.SetFinalStrikeEnabled(!configuration.AutoFinalStrikeEnabled),
                "Master switch for Finishing Blow. Set individual toggles for Whistles 1/2/3 and the pet HP threshold in the Auto Rotation tab; when "Wait for Release" is on, it waits for the current beast to use Release first.",
                ref column);
            DrawOverlayAdvancedToggle("Sustained Provoke", IsArenaRuleEnabled(46751, 2413),
                () => ToggleArenaRule(46751, 2413),
                "Toggles the Sustained Provoke rule in Rule Mode; only effective in Beast Arena zones 1339-1343.",
                ref column,
                yellowWhenEnabled: true);
            DrawOverlayAdvancedToggle("Sustained Taunt", IsArenaRuleEnabled(46750, 5586),
                () => ToggleArenaRule(46750, 5586),
                "Toggles the Sustained Taunt rule in Rule Mode; only effective in Beast Arena zones 1339-1343.",
                ref column,
                yellowWhenEnabled: true);
            return;
        }

        var beastHeartEnabled = configuration.BeastHeartCooperationEnabled;
        if (ImGui.Checkbox("Taming Link (Yellow Orb)", ref beastHeartEnabled))
        {
            configuration.BeastHeartCooperationEnabled = beastHeartEnabled;
            if (beastHeartEnabled)
            {
                configuration.BeastSoulCooperationEnabled = false;
            }
            configuration.Save();
        }

        var beastSoulEnabled = configuration.BeastSoulCooperationEnabled;
        if (ImGui.Checkbox("Beast Spirit Link (Blue Orb)", ref beastSoulEnabled))
        {
            configuration.BeastSoulCooperationEnabled = beastSoulEnabled;
            if (beastSoulEnabled)
            {
                configuration.BeastHeartCooperationEnabled = false;
            }
            configuration.Save();
        }

        var physicalThirdFormEnabled = configuration.PhysicalThirdFormEnabled;
        if (ImGui.Checkbox("Convergence (Physical)", ref physicalThirdFormEnabled))
        {
            configuration.PhysicalThirdFormEnabled = physicalThirdFormEnabled;
            if (physicalThirdFormEnabled)
            {
                configuration.MagicalThirdFormEnabled = false;
            }
            configuration.Save();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Convergence · Physical");
        }

        var magicalThirdFormEnabled = configuration.MagicalThirdFormEnabled;
        if (ImGui.Checkbox("Convergence (Magic)", ref magicalThirdFormEnabled))
        {
            configuration.MagicalThirdFormEnabled = magicalThirdFormEnabled;
            if (magicalThirdFormEnabled)
            {
                configuration.PhysicalThirdFormEnabled = false;
            }
            configuration.Save();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Convergence · Magic");
        }

        var autoWhistleEnabled = configuration.AutoWhistleEnabled;
        if (ImGui.Checkbox("Auto Beast Whistle", ref autoWhistleEnabled))
        {
            configuration.AutoWhistleEnabled = autoWhistleEnabled;
            configuration.Save();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("When no beast is out, cycles Whistle 1→2→3 to use the first available one; waits 1 second after use to confirm the summon.");
        }

        DrawCompactSettingCheckbox("Rally", "Rally · Auto-Use When Ready: evaluated when Heart of Taming is 0, and also when Heart of Taming is 3 and Skill Power is 0. Automatically uses Rally (44905) whenever the game allows it.", nameof(configuration.AutoDrumEnabled), configuration.AutoDrumEnabled);
        DrawCompactSettingCheckbox("Cheer", "Cheer · Auto-Use When Ready: evaluated when Heart of the Beast Spirit is 0, and also when it's 3 and Beast Power is 0. Automatically uses Cheer (44904) whenever the game allows it.", nameof(configuration.AutoCheerEnabled), configuration.AutoCheerEnabled);
        DrawCompactSettingCheckbox("Borrow", "Borrow · Auto-Use When Ready: borrows the current beast's Instinct skill (44895); after borrowing, Beast Skill becomes the borrowed skill. Off by default and not currently part of Auto Rotation.", nameof(configuration.AutoBorrowEnabled), configuration.AutoBorrowEnabled);
        DrawCompactSettingCheckbox("Beast Skill", "Beast Skill · Auto-Use When Ready: uses the borrowed Instinct skill (adjusted 44886). Off by default and not currently part of Auto Rotation.", nameof(configuration.AutoBeastSkillEnabled), configuration.AutoBeastSkillEnabled);

        var autoRecoveryItemEnabled = configuration.AutoRecoveryItemEnabled;
        if (ImGui.Checkbox("Auto-Use Recovery Item at Low HP", ref autoRecoveryItemEnabled))
        {
            configuration.AutoRecoveryItemEnabled = autoRecoveryItemEnabled;
            configuration.Save();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Only effective in Beast Arena Trials fights; when your HP drops below the threshold, uses Trial recovery items in this priority: Recovery Set → Recovery Potion Lv4/3/2/1 → Recovery Powder Lv3/2/1 → Beast Vigor Potion → Vampire Fang. Vampire Fang requires a current enemy target and has a 2-second cooldown after a successful use. Off by default.");
        }
        if (configuration.AutoRecoveryItemEnabled)
        {
            ImGui.SetNextItemWidth(70f);
            var recoveryThreshold = configuration.AutoRecoveryItemHpThreshold;
            if (ImGui.InputFloat("Recovery Item HP Threshold", ref recoveryThreshold, 0f, 0f, "%.0f%%"))
            {
                configuration.AutoRecoveryItemHpThreshold = Math.Clamp(recoveryThreshold, 1f, 100f);
                configuration.Save();
            }
            DrawCompactSettingCheckbox(
                "Recovery Item Silent Chat Alert",
                "Sends a silent-chat alert when an automatic low-HP item use succeeds or fails; success is determined by an HP increase after the request. Off by default.",
                nameof(configuration.AutoRecoveryItemDiagnosticsEnabled),
                configuration.AutoRecoveryItemDiagnosticsEnabled);
        }

        if (compactFinalStrike)
        {
            var finalStrikeEnabled = autoCaptureService.FinalStrikeEnabled;
            if (ImGui.Checkbox("Finishing Blow", ref finalStrikeEnabled))
            {
                autoCaptureService.SetFinalStrikeEnabled(finalStrikeEnabled);
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("HP settings for Whistles 1, 2, and 3 are in the Auto Rotation tab under Advanced Skills.");
            }
        }
        else
        {
            DrawFinalStrikeSettings("Whistle 1", nameof(configuration.AutoFinalStrikeWhistleOneEnabled), configuration.AutoFinalStrikeWhistleOneEnabled,
                nameof(configuration.AutoFinalStrikeWhistleOneHpThreshold), configuration.AutoFinalStrikeWhistleOneHpThreshold);
            DrawFinalStrikeSettings("Whistle 2", nameof(configuration.AutoFinalStrikeWhistleTwoEnabled), configuration.AutoFinalStrikeWhistleTwoEnabled,
                nameof(configuration.AutoFinalStrikeWhistleTwoHpThreshold), configuration.AutoFinalStrikeWhistleTwoHpThreshold);
            DrawFinalStrikeSettings("Whistle 3", nameof(configuration.AutoFinalStrikeWhistleThreeEnabled), configuration.AutoFinalStrikeWhistleThreeEnabled,
                nameof(configuration.AutoFinalStrikeWhistleThreeHpThreshold), configuration.AutoFinalStrikeWhistleThreeHpThreshold);

            var waitForRelease = configuration.AutoFinalStrikeWaitForRelease;
            if (ImGui.Checkbox("Finishing Blow · Wait for Release", ref waitForRelease))
            {
                configuration.AutoFinalStrikeWaitForRelease = waitForRelease;
                configuration.Save();
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("When enabled, Finishing Blow is only used once the current beast's Release skill is unavailable; Auto Release doesn't need to be on.");
            }
        }

        var releaseEnabled = configuration.AutoReleaseEnabled;
        if (ImGui.Checkbox("Release", ref releaseEnabled))
        {
            configuration.AutoReleaseEnabled = releaseEnabled;
            configuration.Save();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Master switch for Release; individual toggles for Whistles 1/2/3 and the target HP threshold are set below.");
        }
        if (!compactFinalStrike)
        {
            DrawReleaseSettings("Whistle 1", nameof(configuration.AutoReleaseWhistleOneEnabled), configuration.AutoReleaseWhistleOneEnabled,
                nameof(configuration.AutoReleaseWhistleOneTargetHpThreshold), configuration.AutoReleaseWhistleOneTargetHpThreshold);
            DrawReleaseSettings("Whistle 2", nameof(configuration.AutoReleaseWhistleTwoEnabled), configuration.AutoReleaseWhistleTwoEnabled,
                nameof(configuration.AutoReleaseWhistleTwoTargetHpThreshold), configuration.AutoReleaseWhistleTwoTargetHpThreshold);
            DrawReleaseSettings("Whistle 3", nameof(configuration.AutoReleaseWhistleThreeEnabled), configuration.AutoReleaseWhistleThreeEnabled,
                nameof(configuration.AutoReleaseWhistleThreeTargetHpThreshold), configuration.AutoReleaseWhistleThreeTargetHpThreshold);

            var releaseBossOnly = configuration.AutoReleaseBossOnly;
            if (ImGui.Checkbox("Release · Bosses Only", ref releaseBossOnly))
            {
                configuration.AutoReleaseBossOnly = releaseBossOnly;
                configuration.Save();
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("When enabled, the current whistle can only use Release based on the target HP threshold when the target's max HP is strictly greater than your max HP × 5. Off by default.");
            }
        }

        // Temporarily hide the Whistle Rotation Combo entry point; implementation kept for future restoration.
        // var whistleRotationEnabled = configuration.WhistleRotationEnabled;
        // if (ImGui.Checkbox("Whistle Rotation Combo", ref whistleRotationEnabled))
        // {
        //     configuration.WhistleRotationEnabled = whistleRotationEnabled;
        //     configuration.Save();
        // }
        // if (ImGui.IsItemHovered())
        // {
        //     ImGui.BeginTooltip();
        //     ImGui.TextUnformatted("Whistle 1 (out of combat) → Release → Finishing Blow → Whistle 2 → Release → Finishing Blow → Whistle 3 → Release");
        //     ImGui.TextDisabled("Automatically turns off when finished and waits for Whistle 1's cooldown to end.");
        //     ImGui.EndTooltip();
        // }
        // ImGui.TextDisabled($"Status: {autoCaptureService.WhistleRotationStatus}");

        ImGui.Unindent();
    }

    private static void DrawOverlayAdvancedToggle(
        string label,
        bool enabled,
        System.Action toggle,
        string tooltip,
        ref int column,
        bool yellowWhenEnabled = false,
        int columnCount = 2)
    {
        if (column % columnCount != 0) ImGui.SameLine();
        var background = enabled
            ? yellowWhenEnabled
                ? new Vector4(0.62f, 0.52f, 0.22f, 1f)
                : new Vector4(0.12f, 0.35f, 0.28f, 1f)
            : new Vector4(0.18f, 0.2f, 0.23f, 1f);
        var textColor = enabled
            ? yellowWhenEnabled
                ? new Vector4(1f, 0.95f, 0.68f, 1f)
                : new Vector4(0.55f, 1f, 0.72f, 1f)
            : new Vector4(0.7f, 0.72f, 0.76f, 1f);
        ImGui.PushStyleColor(ImGuiCol.Button, background);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, enabled
            ? yellowWhenEnabled
                ? new Vector4(0.72f, 0.61f, 0.28f, 1f)
                : new Vector4(0.16f, 0.45f, 0.35f, 1f)
            : new Vector4(0.25f, 0.28f, 0.33f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, background);
        ImGui.PushStyleColor(ImGuiCol.Text, textColor);
        if (ImGui.Button($"{label}##overlay-advanced-{label}", new Vector2(96f, 28f))) toggle();
        ImGui.PopStyleColor(4);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(tooltip);
        column = (column + 1) % columnCount;
    }

    private static void DrawOverlayAdvancedPlaceholder(string label, ref int column, int columnCount = 2)
    {
        if (column % columnCount != 0) ImGui.SameLine();
        ImGui.BeginDisabled();
        ImGui.Button($"{label}##overlay-advanced-placeholder", new Vector2(96f, 28f));
        ImGui.EndDisabled();
        column = (column + 1) % columnCount;
    }

    private void ToggleBoolean(string propertyName)
    {
        switch (propertyName)
        {
            case nameof(configuration.AutoDrumEnabled): configuration.AutoDrumEnabled = !configuration.AutoDrumEnabled; break;
            case nameof(configuration.AutoCheerEnabled): configuration.AutoCheerEnabled = !configuration.AutoCheerEnabled; break;
            case nameof(configuration.AutoWhistleEnabled): configuration.AutoWhistleEnabled = !configuration.AutoWhistleEnabled; break;
            case nameof(configuration.AutoReleaseEnabled): configuration.AutoReleaseEnabled = !configuration.AutoReleaseEnabled; break;
            case nameof(configuration.AutoSafeShieldEnabled): configuration.AutoSafeShieldEnabled = !configuration.AutoSafeShieldEnabled; break;
            case nameof(configuration.AutoRecoveryItemEnabled): configuration.AutoRecoveryItemEnabled = !configuration.AutoRecoveryItemEnabled; break;
            case nameof(configuration.AutoRecoveryItemDiagnosticsEnabled): configuration.AutoRecoveryItemDiagnosticsEnabled = !configuration.AutoRecoveryItemDiagnosticsEnabled; break;
        }
        configuration.Save();
    }

    private void ToggleCooperation(bool heart)
    {
        if (heart)
        {
            configuration.BeastHeartCooperationEnabled = !configuration.BeastHeartCooperationEnabled;
            if (configuration.BeastHeartCooperationEnabled) configuration.BeastSoulCooperationEnabled = false;
        }
        else
        {
            configuration.BeastSoulCooperationEnabled = !configuration.BeastSoulCooperationEnabled;
            if (configuration.BeastSoulCooperationEnabled) configuration.BeastHeartCooperationEnabled = false;
        }
        configuration.Save();
    }

    private void ToggleThirdForm(bool physical)
    {
        if (physical)
        {
            configuration.PhysicalThirdFormEnabled = !configuration.PhysicalThirdFormEnabled;
            if (configuration.PhysicalThirdFormEnabled) configuration.MagicalThirdFormEnabled = false;
        }
        else
        {
            configuration.MagicalThirdFormEnabled = !configuration.MagicalThirdFormEnabled;
            if (configuration.MagicalThirdFormEnabled) configuration.PhysicalThirdFormEnabled = false;
        }
        configuration.Save();
    }

    private bool IsArenaRuleEnabled(uint actionId, uint conditionId)
        => FindArenaRule(actionId, conditionId)?.Enabled == true;

    private void ToggleArenaRule(uint actionId, uint conditionId)
    {
        var rule = FindArenaRule(actionId, conditionId);
        if (rule == null)
        {
            return;
        }

        rule.Enabled = !rule.Enabled;
        configuration.Save();
    }

    private BeastmasterRuleDefinition? FindArenaRule(uint actionId, uint conditionId)
        => configuration.RuleSets
            .SelectMany(ruleSet => ruleSet.Rules)
            .FirstOrDefault(rule => rule.ConditionType == BeastmasterRuleConditionType.SelfStatus
                && rule.StatusCondition == BeastmasterRuleStatusCondition.Missing
                && rule.ActionId == actionId
                && rule.ConditionId == conditionId);

    private void DrawSequenceSettings()
    {
        if (!ImGui.CollapsingHeader("Skill Sequence##BeastmasterSequenceSettings"))
        {
            return;
        }

        ImGui.Indent();
        var enabled = sequenceService.Enabled;
        if (ImGui.Checkbox("Enable Skill Sequence", ref enabled))
        {
            sequenceService.SetEnabled(enabled);
        }
        DrawSequenceSelector("Current Sequence", "##settings-sequence-selector");
        ImGui.TextDisabled($"Current Sequence: {sequenceService.CurrentSequenceName}");
        ImGui.TextDisabled($"Status: {sequenceService.Status}");
        if (sequenceService.IsControlling && ImGui.Button("Abort Skill Sequence"))
        {
            sequenceService.Abort("Manually aborted, waiting for the next party countdown");
        }
        ImGui.Unindent();
    }

    private void DrawSequenceSelector(string label, string id)
    {
        var sequences = configuration.Sequences;
        if (sequences.Count == 0)
        {
            return;
        }

        var selected = Math.Clamp(configuration.SelectedSequenceIndex, 0, sequences.Count - 1);
        var names = string.Join('\0', sequences.Select(sequence => sequence.Name)) + '\0';
        ImGui.SetNextItemWidth(id.Contains("overlay", StringComparison.Ordinal) ? 150f : 220f);
        if (ImGui.Combo($"{label}{id}", ref selected, names))
        {
            configuration.SelectedSequenceIndex = selected;
            configuration.Save();
            if (sequenceService.IsControlling)
            {
                sequenceService.Abort("Sequence switched, waiting for the next party countdown");
            }
        }
    }

    private void DrawCompactFinalStrikeToggle(string label, string propertyName, bool value)
    {
        if (ImGui.Checkbox($"{label}##overlay-{propertyName}", ref value))
        {
            SetFinalStrikeEnabled(propertyName, value);
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip($"{label} Finishing Blow; set the HP threshold in the Auto Rotation tab.");
        }
    }

    private void DrawFinalStrikeSettings(
        string label,
        string enabledProperty,
        bool enabled,
        string thresholdProperty,
        float threshold)
    {
        if (ImGui.Checkbox($"Finishing Blow · {label}##{enabledProperty}", ref enabled))
        {
            SetFinalStrikeEnabled(enabledProperty, enabled);
        }

        threshold = Math.Clamp(threshold, 1f, 100f);
        SetThresholdInputLayout();
        if (ImGui.InputFloat($"##{thresholdProperty}", ref threshold, 1f, 5f, "%.0f%%"))
        {
            SetFinalStrikeThreshold(thresholdProperty, threshold);
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip($"Uses Finishing Blow for {label} when the pet's HP is at or below this threshold, ranging 1%-100%.");
        }
    }

    private void SetFinalStrikeEnabled(string propertyName, bool value)
    {
        switch (propertyName)
        {
            case nameof(configuration.AutoFinalStrikeWhistleOneEnabled):
                configuration.AutoFinalStrikeWhistleOneEnabled = value;
                break;
            case nameof(configuration.AutoFinalStrikeWhistleTwoEnabled):
                configuration.AutoFinalStrikeWhistleTwoEnabled = value;
                break;
            case nameof(configuration.AutoFinalStrikeWhistleThreeEnabled):
                configuration.AutoFinalStrikeWhistleThreeEnabled = value;
                break;
        }
        configuration.Save();
    }

    private void SetFinalStrikeThreshold(string propertyName, float value)
    {
        value = Math.Clamp(value, 1f, 100f);
        switch (propertyName)
        {
            case nameof(configuration.AutoFinalStrikeWhistleOneHpThreshold):
                configuration.AutoFinalStrikeWhistleOneHpThreshold = value;
                break;
            case nameof(configuration.AutoFinalStrikeWhistleTwoHpThreshold):
                configuration.AutoFinalStrikeWhistleTwoHpThreshold = value;
                break;
            case nameof(configuration.AutoFinalStrikeWhistleThreeHpThreshold):
                configuration.AutoFinalStrikeWhistleThreeHpThreshold = value;
                break;
        }
        configuration.Save();
    }

    private void DrawReleaseSettings(
        string label,
        string enabledProperty,
        bool enabled,
        string thresholdProperty,
        float threshold)
    {
        if (ImGui.Checkbox($"Release · {label}##{enabledProperty}", ref enabled))
        {
            SetReleaseEnabled(enabledProperty, enabled);
        }

        threshold = Math.Clamp(threshold, 1f, 100f);
        SetThresholdInputLayout();
        if (ImGui.InputFloat($"##{thresholdProperty}", ref threshold, 1f, 5f, "%.0f%%"))
        {
            SetReleaseThreshold(thresholdProperty, threshold);
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip($"Allows {label} to use Release when the target's HP is at or below this threshold, ranging 1%-100%.");
        }
    }

    private static void SetThresholdInputLayout()
    {
        var style = ImGui.GetStyle();
        var inputWidth = Math.Max(
            100f,
            ImGui.CalcTextSize("100%").X
            + style.FramePadding.X * 2f
            + ImGui.GetFrameHeight() * 2f
            + style.ItemInnerSpacing.X * 2f);
        var sameLineWidth = ImGui.GetWindowPos().X
            + ImGui.GetWindowContentRegionMax().X
            - ImGui.GetItemRectMax().X
            - style.ItemSpacing.X;

        if (sameLineWidth >= inputWidth)
        {
            ImGui.SameLine();
        }

        ImGui.SetNextItemWidth(inputWidth);
    }

    private void SetReleaseEnabled(string propertyName, bool value)
    {
        switch (propertyName)
        {
            case nameof(configuration.AutoReleaseWhistleOneEnabled):
                configuration.AutoReleaseWhistleOneEnabled = value;
                break;
            case nameof(configuration.AutoReleaseWhistleTwoEnabled):
                configuration.AutoReleaseWhistleTwoEnabled = value;
                break;
            case nameof(configuration.AutoReleaseWhistleThreeEnabled):
                configuration.AutoReleaseWhistleThreeEnabled = value;
                break;
        }
        configuration.Save();
    }

    private void SetReleaseThreshold(string propertyName, float value)
    {
        value = Math.Clamp(value, 1f, 100f);
        switch (propertyName)
        {
            case nameof(configuration.AutoReleaseWhistleOneTargetHpThreshold):
                configuration.AutoReleaseWhistleOneTargetHpThreshold = value;
                break;
            case nameof(configuration.AutoReleaseWhistleTwoTargetHpThreshold):
                configuration.AutoReleaseWhistleTwoTargetHpThreshold = value;
                break;
            case nameof(configuration.AutoReleaseWhistleThreeTargetHpThreshold):
                configuration.AutoReleaseWhistleThreeTargetHpThreshold = value;
                break;
        }
        configuration.Save();
    }

    private void DrawCaptureHpThreshold()
    {
        var threshold = Math.Clamp(configuration.CaptureHpThreshold, 1f, 100f);
        ImGui.SetNextItemWidth(180f);
        if (ImGui.InputFloat("Capture HP Threshold", ref threshold, 1f, 5f, "%.0f%%"))
        {
            threshold = Math.Clamp(threshold, 1f, 100f);
            configuration.CaptureHpThreshold = threshold;
            configuration.Save();
        }

        ImGui.SameLine();
        ImGui.TextDisabled("Uses Capture when the target is below this HP");
    }

    private void DrawBeastmasterGauge()
    {
        ImGui.Separator();
        ImGui.Text("Current Gauge");
        ImGui.TextDisabled("Read-only display of the current Beastmaster gauge state; data comes from JobGaugeManager.CurrentGauge.");

        var snapshot = gaugeSnapshot;
        ImGui.TextColored(
            snapshot.Available
                ? new Vector4(0.35f, 0.85f, 0.55f, 1f)
                : new Vector4(0.9f, 0.55f, 0.35f, 1f),
            snapshot.Status);

        if (!snapshot.Available)
        {
            return;
        }

        DrawCurrentSummon(snapshot);

        if (ImGui.BeginTable(
                "BeastmasterGaugeState",
                2,
                ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Field", ImGuiTableColumnFlags.WidthFixed, 150f);
            ImGui.TableSetupColumn("Current Value", ImGuiTableColumnFlags.WidthStretch);
            DrawGaugeRow("Skill Power", $"{snapshot.Tp} / {BeastmasterGaugeSnapshot.MaximumGauge}", "Basic Skill Resource");
            DrawGaugeRow("Beast Power", $"{snapshot.BeastPower} / {BeastmasterGaugeSnapshot.MaximumGauge}", "Beast Heart Skill Resource");
            DrawGaugeRow("Current Whistle", snapshot.WhistleIndex is >= 1 and <= 3
                ? $"No. {snapshot.WhistleIndex}"
                : "Not Summoned", "Current Whistle Type");
            DrawGaugeRow("Pet HP", snapshot.SummonMaxHp > 0
                ? $"{snapshot.SummonHpPercent:0.#}%（{snapshot.SummonCurrentHp}/{snapshot.SummonMaxHp}）"
                : "Unavailable", "Auto Finishing Blow Basis");
            DrawGaugeRow("Heart of Taming", $"{snapshot.BeastHeartStacks} stacks", "Link Gauge");
            DrawGaugeRow("Heart of the Beast Spirit", $"{snapshot.BeastSoulStacks} stacks", "Link Gauge");
            DrawGaugeRow("Black/White Status", GetBlackWhiteStatus(snapshot), "Vital 4599 / Void 4600");
            ImGui.EndTable();
        }

        ImGui.TextDisabled($"Current Decision: {GetGaugeDecision(snapshot)}");
        ImGui.TextDisabled($"Advanced Skill Judgment: {autoCaptureService.AdvancedActionStatus}");

    }

    private static void DrawCurrentSummon(BeastmasterGaugeSnapshot snapshot)
    {
        if (snapshot.SummonDataId == 0)
        {
            ImGui.TextDisabled("Current Beast: Not Summoned");
            return;
        }

        var entry = BeastmasterCatalog.Entries.FirstOrDefault(
            item => item.Number == snapshot.SummonDataId - 18915);
        if (entry == null)
        {
            ImGui.TextDisabled($"Current Beast: {snapshot.SummonName} (DataId {snapshot.SummonDataId})");
            return;
        }

        ImGui.Text("Current Beast");
        ImGui.SameLine();
        ImGui.Text(entry.Name);
        ImGui.SameLine();
        ImGui.TextColored(GetAttributeColor(entry.Attribute), $"[{entry.Attribute}]");
        ImGui.TextDisabled($"Ultimate: {GetActionName(entry.UltimateActionId)} | Release: {GetActionName(entry.ReleaseActionId)}");
    }

    private static string GetGaugeDecision(BeastmasterGaugeSnapshot snapshot)
    {
        if (snapshot.SummonDataId == 0)
        {
            return "Waiting for Summon";
        }

        if (snapshot.Tp < BeastmasterGaugeSnapshot.ComboGaugeRequirement)
        {
            return $"Building Skill Power ({snapshot.Tp}/{BeastmasterGaugeSnapshot.MaximumGauge})";
        }

        if (snapshot.BeastPower < BeastmasterGaugeSnapshot.ComboGaugeRequirement)
        {
            return $"Building Beast Power ({snapshot.BeastPower}/{BeastmasterGaugeSnapshot.MaximumGauge})";
        }

        return snapshot.BeastHeartStacks > 0
            ? "Skill Power and Beast Power are ready — you can plan Link skills around Heart of Taming"
            : "Skill Power and Beast Power are ready — you can execute the combo";
    }

    private static string GetBlackWhiteStatus(BeastmasterGaugeSnapshot snapshot)
        => (snapshot.HasWhiteStatus, snapshot.HasPurpleStatus) switch
        {
            (true, true) => "White (Vital) and Black/Purple (Void) active at the same time",
            (true, false) => "White (Vital)",
            (false, true) => "Black/Purple (Void)",
            _ => "Inactive",
        };

    private string GetThirdFormActionName(BeastmasterGaugeSnapshot snapshot)
    {
        if (!configuration.PhysicalThirdFormEnabled && !configuration.MagicalThirdFormEnabled)
        {
            return "-";
        }

        var actionId = snapshot.BeastHeartStacks >= 3 && (snapshot.HasWhiteStatus || snapshot.HasPurpleStatus)
            ? 44905u
            : (snapshot.HasWhiteStatus, snapshot.HasPurpleStatus) switch
        {
            (true, _) => configuration.PhysicalThirdFormEnabled ? 44931u : 44933u,
            (false, true) => configuration.PhysicalThirdFormEnabled ? 44930u : 44932u,
            _ => 44905u,
        };
        return GetActionName(actionId);
    }

    private static string GetThirdFormReason(BeastmasterGaugeSnapshot snapshot)
        => snapshot.BeastHeartStacks >= 3 && (snapshot.HasWhiteStatus || snapshot.HasPurpleStatus)
            ? $"{GetBlackWhiteStatus(snapshot)}, Heart of Taming at {snapshot.BeastHeartStacks} stacks — Rally is available"
            : snapshot.HasWhiteStatus || snapshot.HasPurpleStatus
                ? $"{GetBlackWhiteStatus(snapshot)}, waiting for Heart of Taming to reach 3 stacks (currently {snapshot.BeastHeartStacks})"
                : "Waiting for Vital (White) or Void (Black/Purple) status";

    private static void DrawGaugeRow(string name, object value, string description)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.Text(name);
        ImGui.TableNextColumn();
        ImGui.Text(value.ToString());
        ImGui.SameLine();
        ImGui.TextDisabled(description);
    }

    private static void DrawBeastmasterGaugeGuide()
    {
        if (!ImGui.CollapsingHeader("Gauge Guide##BeastmasterGaugeGuide"))
        {
            return;
        }

        ImGui.TextDisabled("The notes below are compiled from the Beastmaster job gauge and skill logic. The gauge panel is read-only and never modifies game data.");
        ImGui.Separator();

        DrawGuideTitle("Skill Power / Beast Power");
        ImGui.TextWrapped("Once you learn the Beast Heart trait, the hotbar will show the Beastmaster's dedicated skill gauge. Skill Power increases on consecutive successes; the higher it is, the stronger your weaponskills.");
        ImGui.TextWrapped("Skill Power and Beast Power are the two resources in the Beastmaster job gauge, currently capped at 250 each. The higher they are, the easier it is to meet the requirements for the corresponding skills and Link skills.");

        ImGui.Spacing();
        DrawGuideTitle("Heart of Taming");
        ImGui.TextWrapped("Once you learn the Beast Heart II trait, the screen will show the Heart of Taming status. You gain a stack of Heart of Taming whenever you successfully trigger a Beast Heart Link skill through inspiration.");
        ImGui.TextWrapped("Using Rally consumes all stacks of Heart of Taming and increases the Beastmaster's Skill Power. The more stacks consumed, the greater the increase.");

        ImGui.Spacing();
        DrawGuideTitle("Link Gauge");
        ImGui.TextWrapped("The Link Gauge shows the current state of Beast Heart Link skills. There are four Beast Heart attributes — Flight, Ferocity, Fortitude, and Magic — and each time a Beast Heart skill is used, its corresponding attribute node lights up.");
        ImGui.TextWrapped("Once either the Beastmaster or the beast uses a Beast Heart skill, if the other party uses a skill within 7 seconds, it triggers the Beast Heart Link skill's follow-up damage.");
        ImGui.TextWrapped("The number shown below the Link Gauge is the Link count. The more links you build up, the stronger the Beast Heart Link skill becomes.");

        ImGui.Spacing();
        DrawGuideTitle("Vital / Void");
        ImGui.TextWrapped("Following the attribute cycle "Flight → Ferocity → Fortitude → Magic → Flight" and triggering Beast Heart Link skills clockwise lets you unleash a stronger Vital or Void Link skill.");
        ImGui.TextWrapped("After triggering the second-stage Beast Heart Link skill, the Beastmaster gains the Vital or Void Beast Heart attribute; this lights up the center of the Link Gauge and affects subsequent Link skills.");

        ImGui.Spacing();
        ImGui.TextColored(new Vector4(1f, 0.9f, 0.25f, 1f), "The full job gauge explanation can be viewed anytime in the skill menu.");
    }

    private static void DrawGuideTitle(string title)
    {
        ImGui.TextColored(new Vector4(1f, 0.65f, 0.2f, 1f), title);
    }

    private static void DrawDependency(string name, bool available, string purpose)
    {
        ImGui.TextColored(available ? new Vector4(0.35f, 0.8f, 0.48f, 1f) : new Vector4(0.9f, 0.42f, 0.38f, 1f), available ? "Available" : "Not Loaded");
        ImGui.SameLine();
        ImGui.Text(name);
        ImGui.SameLine();
        ImGui.TextDisabled(purpose);
    }

    private void DrawSettingCheckbox(string label, string description, string key, bool value)
    {
        if (ImGui.Checkbox($"{label}##{key}", ref value))
        {
            switch (key)
            {
                case nameof(configuration.HideCompletedQuests):
                    configuration.HideCompletedQuests = value;
                    break;
                case nameof(configuration.AutoCompleteCatalogFromChat):
                    configuration.AutoCompleteCatalogFromChat = value;
                    break;
                case nameof(configuration.UseFlightNavigation):
                    configuration.UseFlightNavigation = value;
                    break;
                case nameof(configuration.SetFlagOnNavigation):
                    configuration.SetFlagOnNavigation = value;
                    break;
                case nameof(configuration.ShowNavigationLogs):
                    configuration.ShowNavigationLogs = value;
                    break;
                case nameof(configuration.BeastHeartCooperationEnabled):
                    configuration.BeastHeartCooperationEnabled = value;
                    if (value) configuration.BeastSoulCooperationEnabled = false;
                    break;
                case nameof(configuration.BeastSoulCooperationEnabled):
                    configuration.BeastSoulCooperationEnabled = value;
                    if (value) configuration.BeastHeartCooperationEnabled = false;
                    break;
                case nameof(configuration.AutoReleaseEnabled):
                    configuration.AutoReleaseEnabled = value;
                    break;
                case nameof(configuration.AutoDrumEnabled):
                    configuration.AutoDrumEnabled = value;
                    break;
                case nameof(configuration.AutoCheerEnabled):
                    configuration.AutoCheerEnabled = value;
                    break;
                case nameof(configuration.AutoSafeShieldEnabled):
                    configuration.AutoSafeShieldEnabled = value;
                    break;
                case nameof(configuration.ShowGaugeInOverlay):
                    configuration.ShowGaugeInOverlay = value;
                    break;
                case nameof(configuration.OverlayThreeColumnMode):
                    configuration.OverlayThreeColumnMode = value;
                    break;
                case nameof(configuration.AutoOutputDiagnosticsEnabled):
                    configuration.AutoOutputDiagnosticsEnabled = value;
                    break;
            }

            configuration.Save();
        }

        ImGui.TextDisabled(description);
    }

    private void DrawCompactSettingCheckbox(string label, string description, string key, bool value)
    {
        if (ImGui.Checkbox($"{label}##{key}", ref value))
        {
            switch (key)
            {
                case nameof(configuration.AutoDrumEnabled):
                    configuration.AutoDrumEnabled = value;
                    break;
                case nameof(configuration.AutoCheerEnabled):
                    configuration.AutoCheerEnabled = value;
                    break;
                case nameof(configuration.AutoBorrowEnabled):
                    configuration.AutoBorrowEnabled = value;
                    break;
                case nameof(configuration.AutoBeastSkillEnabled):
                    configuration.AutoBeastSkillEnabled = value;
                    break;
                case nameof(configuration.AutoRecoveryItemDiagnosticsEnabled):
                    configuration.AutoRecoveryItemDiagnosticsEnabled = value;
                    break;
            }
            configuration.Save();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(description);
        }
    }

    private void DrawDebug()
    {
        ImGui.Text("DEBUG");
        ImGui.TextDisabled("Select a data type, then load it; the result is automatically copied to the clipboard.");
        ImGui.Separator();

        ImGui.Text("Skill Status");
        ImGui.SetNextItemWidth(Math.Max(120f, ImGui.GetContentRegionAvail().X - 72f));
        ImGui.InputText("##DebugActionId", ref debugActionId, 10);
        ImGui.SameLine();
        if (ImGui.Button("Query##DebugActionStatus"))
        {
            try
            {
                SetDebugResult(uint.TryParse(debugActionId, out var actionId)
                    ? debugDataService.GetActionStatusDebug(actionId, debugUseAdjustedActionId)
                    : "Skill Status\nPlease enter a valid numeric ActionId.");
            }
            catch (Exception ex)
            {
                SetDebugResult($"Skill Status\nQuery error: {ex.Message}");
            }
        }

        ImGui.Checkbox("Use GetAdjustedActionId", ref debugUseAdjustedActionId);
        ImGui.Spacing();

        ImGui.Text("Keyword Search");
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputText("##DebugQuery", ref debugQuery, 128);

        DrawDebugActionRow(
            "##DebugSearchType",
            ref debugSearchType,
            "Class/Job\0Zone\0Quest\0Item\0NPC\0Monster\0Duty\0",
            "Query##DebugSearch",
            RunDebugSearch);

        ImGui.Spacing();
        ImGui.Text("Project Data");
        DrawDebugActionRow(
            "##DebugProjectDataType",
            ref debugProjectDataType,
            "Beast Tamer Quest\0All Current Quest States\0Beastmaster Quest Chain\0Catalog Duty IDs\0Auto-Capture IDs\0Beast Attribute Mapping\0Beast Catalog Client Data\0Recommended Gear Item IDs\0Beast Recovery Item Scan\0Content Item Container Scan\0XBM UI Scan\0XBM Item Structure\0Trial Item List\0Beast Level EXP Structure\0Beast Arena Results Level EXP\0Beast Formation Structure\0Beastmaster Progression Data Module\0Trial Item ExecuteSlot Test (uses slot 0)\0Trial Item UseAction Test (uses slot 0)\0Hotbar Trial Item Scan\0Execute Real Trial Hotbar Slot (uses item)\0ExecuteSlotById Test (hotbar 2, uses item)\0",
            "Load##DebugProjectData",
            RunDebugProjectData);

        ImGui.Spacing();
        ImGui.Text("Current State");
        DrawDebugActionRow(
            "##DebugCurrentStateType",
            ref debugCurrentStateType,
            "Raw Beastmaster Gauge Data\0Current Target State\0Current Combo State\0Link Validation Data\0Current Character\0Current Location\0Target Capture Judgment\0Auto Rotation State\0Skill Sequence Validation Data\0",
            "Load##DebugCurrentState",
            RunDebugCurrentState);

        ImGui.Spacing();
        DrawUseActionScanRow();

        ImGui.Separator();
        if (ImGui.BeginChild("DebugResult", Vector2.Zero, true))
        {
            ImGui.TextUnformatted(debugResult);
        }

        ImGui.EndChild();
    }

    private void DrawUseActionScanRow()
    {
        ImGui.SetNextItemWidth(Math.Max(120f, ImGui.GetContentRegionAvail().X - 72f));
        ImGui.Text($"ActionId Scan: {(debugDataService.IsUseActionScanActive ? "Running..." : "Idle")}");
        ImGui.SameLine();
        if (ImGui.Button("Scan Slot 0 ActionId 46959-46980##StartUseActionScan"))
        {
            SetDebugResult(debugDataService.StartUseActionScan(0, 46959, 46980));
        }

        if (ImGui.Button(debugDataService.IsCaptureActive
                ? "Stop Trial Click Capture##StopCapture"
                : "Start Trial Click Capture##StartCapture"))
        {
            SetDebugResult(debugDataService.IsCaptureActive
                ? debugDataService.StopCrucibleClickCapture()
                : debugDataService.StartCrucibleClickCapture());
        }
    }

    private void DriveUseActionScan()
    {
        debugDataService.UpdateUseActionScan();
        while (debugDataService.TryTakeUseActionScanLog(out var log))
        {
            DalamudApi.ChatGui.Print($"[Beastmaster Recovery Item Diagnostics] {log}");
        }
    }

    private static void DrawDebugActionRow(
        string comboId,
        ref int selectedIndex,
        string options,
        string buttonLabel,
        System.Action action)
    {
        ImGui.SetNextItemWidth(Math.Max(120f, ImGui.GetContentRegionAvail().X - 72f));
        ImGui.Combo(comboId, ref selectedIndex, options);
        ImGui.SameLine();
        if (ImGui.Button(buttonLabel))
        {
            action();
        }
    }

    private void RunDebugSearch()
    {
        SetDebugResult(debugSearchType switch
        {
            0 => debugDataService.FindClassJobs(debugQuery),
            1 => debugDataService.FindTerritories(debugQuery),
            2 => debugDataService.FindQuests(debugQuery),
            3 => debugDataService.FindItems(debugQuery),
            4 => debugDataService.FindNpcs(debugQuery),
            5 => debugDataService.FindMonsters(debugQuery),
            6 => debugDataService.FindDuties(debugQuery),
            _ => "Unknown query type.",
        });
    }

    private void RunDebugProjectData()
    {
        SetDebugResult(debugProjectDataType switch
        {
            0 => debugDataService.FindQuests("Beast Tamer"),
            1 => questService.GetActiveQuestsDebug(),
            2 => debugDataService.FindBeastmasterQuestChain(),
            3 => debugDataService.FindCatalogDuties(),
            4 => debugDataService.FindAutoCaptureData(),
            5 => debugDataService.FindBeastmasterAttributes(),
            6 => debugDataService.GetBeastmasterCatalogProbe(),
            7 => debugDataService.FindRecommendedEquipmentIds(),
            8 => debugDataService.FindBeastmasterRecoveryItems(),
            9 => debugDataService.FindContentInventoryContainers(),
            10 => debugDataService.GetXbmAddonProbe(),
            11 => debugDataService.GetXbmItemStructureProbe(),
            12 => debugDataService.GetCrucibleItemList(),
            13 => debugDataService.GetBeastLevelExperienceProbe(),
            14 => debugDataService.GetBeastResultProgressionProbe(),
            15 => debugDataService.GetPetPartyStructureProbe(),
            16 => debugDataService.GetXbmModuleProbe(),
            17 => debugDataService.TestCrucibleExecuteSlot(0),
            18 => debugDataService.TestCrucibleUseAction(0),
            19 => debugDataService.ScanHotbarsForCrucibleItems(),
            20 => debugDataService.TestExecuteRealCrucibleSlot(2, 0),
            21 => debugDataService.TestExecuteSlotById(2, 11),
            _ => "Unknown project data type.",
        });
    }

    private void RunDebugCurrentState()
    {
        SetDebugResult(debugCurrentStateType switch
        {
            0 => debugDataService.GetBeastmasterGaugeRaw(),
            1 => debugDataService.GetCurrentTargetDebug(),
            2 => debugDataService.GetComboDebug(),
            3 => debugDataService.GetCooperationValidationDebug(),
            4 => debugDataService.GetCharacter(),
            5 => debugDataService.GetLocation(),
            6 => debugDataService.GetCaptureCheckDebug(),
            7 => debugDataService.GetAutoOutputConditionDebug(),
            8 => debugDataService.GetSkillSequenceValidationDebug(),
            _ => "Unknown current state type.",
        });
    }

    private void SetDebugResult(string result)
    {
        debugResult = result;
        ImGui.SetClipboardText(result);
    }
}
