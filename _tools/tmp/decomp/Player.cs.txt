using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using Kingmaker.Achievements;
using Kingmaker.AreaLogic.Etudes;
using Kingmaker.AreaLogic.QuestSystem;
using Kingmaker.Armies;
using Kingmaker.Armies.TacticalCombat;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Area;
using Kingmaker.Blueprints.Items;
using Kingmaker.Blueprints.Root;
using Kingmaker.Console.PS5.PSNObjects;
using Kingmaker.Controllers;
using Kingmaker.Controllers.Rest.State;
using Kingmaker.Corruption;
using Kingmaker.Craft;
using Kingmaker.Crusade.GlobalMagic;
using Kingmaker.DLC;
using Kingmaker.DialogSystem.State;
using Kingmaker.Dungeon;
using Kingmaker.Dungeon.Actions;
using Kingmaker.Dungeon.Units;
using Kingmaker.EntitySystem;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Persistence;
using Kingmaker.EntitySystem.Persistence.JsonUtility;
using Kingmaker.EntitySystem.Persistence.Versioning;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.Enums;
using Kingmaker.Formations;
using Kingmaker.GameModes;
using Kingmaker.Globalmap.Blueprints;
using Kingmaker.Globalmap.State;
using Kingmaker.Globalmap.View;
using Kingmaker.Inspect;
using Kingmaker.Items;
using Kingmaker.Kingdom;
using Kingmaker.Kingdom.Blueprints;
using Kingmaker.Modding;
using Kingmaker.PubSubSystem;
using Kingmaker.QA;
using Kingmaker.QA.Statistics;
using Kingmaker.Settings;
using Kingmaker.Settings.Difficulty;
using Kingmaker.Tutorial;
using Kingmaker.UI;
using Kingmaker.UI.SettingsUI;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Class.LevelUp;
using Kingmaker.UnitLogic.Groups;
using Kingmaker.UnitLogic.Parts;
using Kingmaker.Utility;
using Kingmaker.View;
using Kingmaker.View.MapObjects;
using Kingmaker.View.Spawners;
using Kingmaker.Visual.Critters;
using Newtonsoft.Json;
using Owlcat.Runtime.Core.Logging;
using Owlcat.Runtime.Core.Utils;
using Owlcat.Runtime.Visual.Effects.WeatherSystem;
using Pathfinding.Util;
using UnityEngine;

namespace Kingmaker;

[JsonObject]
public class Player : EntityDataBase, IDisposable
{
	public enum CharactersList
	{
		ActiveUnits = 0,
		Everyone = 1,
		AllDetachedUnits = 3,
		DetachedPartyCharacters = 4,
		PartyCharacters = 5
	}

	public class CampaignImportSettings
	{
		[JsonProperty]
		public bool LetPlayerChooseSave;

		[JsonProperty]
		public bool AutoImportIfOnlyOneSave;
	}

	public class SavedSceneEntry
	{
		[JsonProperty]
		public BlueprintArea Area;

		[JsonProperty]
		public string SceneName;
	}

	public enum GameOverReasonType
	{
		PartyIsDefeated = 0,
		EssentialUnitIsDead = 2,
		KingdomIsDestroyed = 3,
		Won = 4,
		QuestFailed = 5
	}

	public class WeatherData
	{
		[JsonProperty]
		public InclemencyType CurrentWeather;

		[JsonProperty]
		public TimeSpan NextWeatherChange;

		public InclemencyType ActualWeather
		{
			get
			{
				if (WeatherController.Instance == null)
				{
					return InclemencyType.Clear;
				}
				return WeatherController.Instance.ActualInclemency;
			}
		}
	}

	public class GlobalMapHelper
	{
		private readonly Player m_Player;

		public GlobalMapState LastActivated
		{
			get
			{
				GlobalMapState globalMapState = null;
				foreach (GlobalMapState allGlobalMap in m_Player.AllGlobalMaps)
				{
					if (globalMapState == null || allGlobalMap.LastActivationTime > globalMapState.LastActivationTime)
					{
						globalMapState = allGlobalMap;
					}
				}
				return globalMapState;
			}
		}

		public RegionId CurrentRegion => LastActivated?.Player.CurrentRegion ?? RegionId.None;

		public GlobalMapZone CurrentZone => GlobalMapView.Instance?.CurrentZone ?? GetGlobalMapZone(CurrentPosition);

		public GlobalMapPosition CurrentPosition => LastActivated.PlayerPosition;

		public GlobalMapHelper(Player player)
		{
			m_Player = player;
		}

		public GlobalMapPointState GetPointState(BlueprintGlobalMapPoint blueprint)
		{
			return m_Player.GetGlobalMap(blueprint.GlobalMap)?.GetPointState(blueprint);
		}

		public GlobalMapEdgeState GetEdgeState(BlueprintGlobalMapEdge blueprint)
		{
			return m_Player.GetGlobalMap(blueprint.GlobalMap)?.GetEdgeState(blueprint);
		}

		public static GlobalMapZone GetGlobalMapZone(GlobalMapPosition position)
		{
			return position?.Location?.GlobalMapZone ?? position?.Edge?.Point1?.GlobalMapZone ?? (position?.Edge?.Point2?.GlobalMapZone).GetValueOrDefault();
		}
	}

	public enum SharedStashType
	{
		MAIN,
		BESMARITES,
		MEMORIES
	}

	[JsonProperty]
	private uint m_MaxGameUniqueId;

	[JsonProperty]
	public DateTime? StartDate;

	[JsonProperty]
	public BlueprintAreaPreset StartPreset;

	public SceneEntitiesState CrossSceneState = new SceneEntitiesState("<cross-scene>");

	[JsonProperty]
	private QuestBook m_QuestBook;

	[JsonProperty]
	private readonly UnlockableFlagsManager m_UnlockableFlags = new UnlockableFlagsManager();

	[JsonProperty]
	private readonly DialogState m_Dialog = new DialogState();

	[JsonProperty]
	private readonly List<GlobalMapState> m_GlobalMaps = new List<GlobalMapState>();

	[JsonProperty]
	private readonly CampingState m_Camping = new CampingState();

	[JsonProperty]
	public readonly CompanionStoriesManager CompanionStories = new CompanionStoriesManager();

	[JsonProperty]
	public EtudesSystem EtudesSystem;

	[JsonProperty]
	private readonly LeadersManager m_LeadersManager = new LeadersManager();

	[JsonProperty]
	private readonly GlobalMagicSpellsManager m_GlobalMapSpellsManager = new GlobalMagicSpellsManager();

	[JsonProperty]
	public Dictionary<string, object> SettingsList = new Dictionary<string, object>();

	[NotNull]
	[JsonProperty]
	public readonly HashSet<BlueprintArea> VisitedAreas = new HashSet<BlueprintArea>();

	[JsonProperty("CurrentArea")]
	public BlueprintArea SavedInArea;

	[JsonProperty("CurrentAreaPart")]
	public BlueprintAreaPart SavedInAreaPart;

	[JsonProperty]
	private Vector3 m_CameraPos;

	[JsonProperty]
	private float m_CameraRot;

	[JsonProperty]
	public readonly List<BlueprintDlcReward> UsedDlcRewards = new List<BlueprintDlcReward>();

	[JsonProperty]
	public readonly List<BlueprintDlcReward> ClaimedDlcRewards = new List<BlueprintDlcReward>();

	[JsonProperty]
	public readonly HashSet<BlueprintCampaign> ImportedCampaigns = new HashSet<BlueprintCampaign>();

	[JsonProperty]
	public readonly Dictionary<BlueprintCampaign, CampaignImportSettings> CampaignsToOfferImport = new Dictionary<BlueprintCampaign, CampaignImportSettings>();

	[JsonProperty]
	public Dictionary<StatType, Dictionary<string, int>> SkillChecks = new Dictionary<StatType, Dictionary<string, int>>();

	[JsonProperty]
	public readonly DungeonState DungeonState = new DungeonState();

	[JsonProperty]
	public readonly PlayerUISettings UISettings = new PlayerUISettings();

	[JsonProperty]
	public readonly MinDifficultyController MinDifficultyController = new MinDifficultyController();

	[JsonProperty]
	public KingdomState Kingdom;

	[JsonProperty]
	public readonly ItemsCollection SharedStash;

	[JsonProperty]
	public ItemsCollection SecondSharedStash;

	[JsonProperty]
	public ItemsCollection ThirdSharedStash;

	[JsonProperty]
	public readonly PSNObjectsManager PSNObjects = new PSNObjectsManager();

	[JsonProperty]
	public readonly RandomEncounterManager REManager = new RandomEncounterManager();

	[JsonProperty]
	public readonly SharedVendorTables SharedVendorTables = new SharedVendorTables();

	[JsonProperty]
	public readonly CorruptionManager Corruption = new CorruptionManager();

	[JsonProperty]
	public readonly AchievementsManager Achievements = new AchievementsManager();

	[JsonProperty]
	public readonly InspectUnitsManager InspectUnitsManager = new InspectUnitsManager();

	[JsonProperty]
	public readonly CraftManager CraftManager = new CraftManager();

	[JsonProperty]
	public readonly List<PlayerUpgradeAction> UpgradeActions = new List<PlayerUpgradeAction>();

	[JsonProperty]
	public readonly List<BlueprintPlayerUpgrader> AppliedPlayerUpgraders = new List<BlueprintPlayerUpgrader>();

	[JsonProperty]
	public readonly List<BlueprintPlayerUpgrader> IgnoredAppliedPlayerUpgraders = new List<BlueprintPlayerUpgrader>();

	[JsonProperty]
	public readonly List<BlueprintPlayerUpgrader> IgnoredNotAppliedPlayerUpgraders = new List<BlueprintPlayerUpgrader>();

	public bool IsClearMainCharacterData;

	public DungeonShowResults.ResultType? Result;

	[JsonProperty]
	public readonly WeatherData Weather = new WeatherData();

	[JsonProperty]
	public readonly WeatherData Wind = new WeatherData();

	public bool IsTurnBasedStateMandatory;

	public bool MandatoryTurnBasedModeState;

	[JsonProperty]
	public readonly StatisticSaveData StatisticData = new StatisticSaveData();

	[JsonProperty]
	public bool IsShowConsoleTooltip = true;

	[JsonProperty]
	public bool IsCameraRotateMode = true;

	[JsonProperty]
	private List<string> m_AdditionalTexNames = new List<string>();

	private bool m_CharacterListsValid;

	private bool m_ShouldFixPartyOnAreaLoaded;

	private readonly List<UnitEntityData> m_Party = new List<UnitEntityData>();

	private readonly List<UnitEntityData> m_PartyAndPets = new List<UnitEntityData>();

	private readonly List<UnitEntityData> m_PartyAndPetsDetached = new List<UnitEntityData>();

	private readonly List<UnitEntityData> m_ActiveCompanions = new List<UnitEntityData>();

	private readonly List<UnitEntityData> m_RemoteCompanions = new List<UnitEntityData>();

	private readonly List<UnitEntityData> m_AllCharacters = new List<UnitEntityData>();

	[CanBeNull]
	private GlobalMapHelper m_GlobalMapHelper;

	[JsonProperty]
	public TimeSpan GameTime { get; set; } = TimeSpan.Zero;

	[JsonProperty]
	public TimeSpan RealTime { get; set; } = TimeSpan.Zero;

	[JsonProperty]
	public TutorialSystem Tutorial { get; private set; }

	[JsonProperty]
	public int ExperienceRatePercent { get; set; } = 100;

	[JsonProperty]
	public List<UnitReference> PartyCharacters { get; private set; } = new List<UnitReference>();

	[JsonProperty]
	public long Money { get; private set; }

	[JsonProperty]
	[CanBeNull]
	public BlueprintUnit Stalker { get; set; }

	[JsonProperty]
	public int Chapter { get; private set; }

	[JsonProperty]
	public TimeSpan ChapterStartDate { get; private set; }

	[JsonProperty]
	public Encumbrance Encumbrance { get; set; }

	[JsonProperty]
	[CanBeNull]
	public HashSet<BlueprintItem> InitialInventory { get; private set; }

	[JsonProperty(PropertyName = "m_MainCharacter")]
	public UnitReference MainCharacter { get; private set; }

	[JsonProperty]
	public int RespecsUsed { get; set; }

	[JsonProperty]
	public CountableFlag AllowMythicPortrait { get; private set; } = new CountableFlag();

	[JsonProperty]
	[CanBeNull]
	public TacticalCombatResults TacticalCombatResults { get; set; }

	[JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
	public bool ModsUser { get; private set; }

	public GameOverReasonType? GameOverReason { get; private set; }

	public PartyFormationManager FormationManager => Ensure<PartyFormationManager>();

	public LeadersManager ArmyLeadersManager => m_LeadersManager;

	public GlobalMagicSpellsManager GlobalMapSpellsManager => m_GlobalMapSpellsManager;

	public UnitGroup Group => PartyCharacters[0].Value.Group;

	public bool CapitalPartyMode
	{
		get
		{
			CountingGuard countingGuard = Game.Instance.LoadedAreaState?.Settings.CapitalPartyMode;
			if (countingGuard == null)
			{
				return false;
			}
			return countingGuard;
		}
	}

	public int MythicExperience => MainCharacter.Value.Progression.MythicExperience;

	public BlueprintCampaign Campaign => SimpleBlueprintExtendAsObject.Or(StartPreset, null)?.Campaign;

	public IReadOnlyList<string> AdditionalTexNames => m_AdditionalTexNames;

	public IEnumerable<UnitEntityData> RemoteCompanions
	{
		get
		{
			UpdateCharacterLists();
			return m_RemoteCompanions;
		}
	}

	public ItemsCollection Inventory { get; private set; }

	public Alignment Alignment => MainCharacter.Value.Descriptor.Alignment.ValueRaw;

	public IEnumerable<UnitEntityData> AllCrossSceneUnits => CrossSceneState.AllEntityData.OfType<UnitEntityData>();

	public CountableFlag DisableMainCharacterRespec { get; } = new CountableFlag();

	public GlobalMapHelper GlobalMap => m_GlobalMapHelper ?? (m_GlobalMapHelper = new GlobalMapHelper(this));

	public List<GlobalMapState> AllGlobalMaps => m_GlobalMaps;

	public List<UnitEntityData> Party
	{
		get
		{
			UpdateCharacterLists();
			return m_Party;
		}
	}

	public List<UnitEntityData> PartyAndPets
	{
		get
		{
			UpdateCharacterLists();
			return m_PartyAndPets;
		}
	}

	public List<UnitEntityData> PartyAndPetsDetached
	{
		get
		{
			UpdateCharacterLists();
			return m_PartyAndPetsDetached;
		}
	}

	public List<UnitEntityData> ActiveCompanions
	{
		get
		{
			UpdateCharacterLists();
			return m_ActiveCompanions;
		}
	}

	public List<UnitEntityData> AllCharacters
	{
		get
		{
			UpdateCharacterLists();
			return m_AllCharacters;
		}
	}

	public int PartyLevel
	{
		get
		{
			float num = 0f;
			int num2 = 0;
			for (int i = 0; i < PartyCharacters.Count; i++)
			{
				UnitEntityData value = PartyCharacters[i].Value;
				if (value != null && value.IsInGame)
				{
					num += (float)value.Descriptor.Progression.CharacterLevel;
					num2++;
				}
			}
			return (int)Math.Round(num / (float)num2);
		}
	}

	public UnlockableFlagsManager UnlockableFlags => m_UnlockableFlags;

	public QuestBook QuestBook
	{
		get
		{
			return m_QuestBook;
		}
		set
		{
			m_QuestBook = value;
		}
	}

	public DialogState Dialog => m_Dialog;

	public CampingState Camping => m_Camping;

	public bool IsInCombat { get; private set; }

	public bool PlayerIsKing
	{
		get
		{
			BlueprintUnlockableFlag kingFlag = BlueprintRoot.Instance.KingFlag;
			if (kingFlag != null)
			{
				return UnlockableFlags.IsUnlocked(kingFlag);
			}
			return false;
		}
	}

	public string GameId { get; set; }

	[JsonProperty]
	public BlueprintAreaEnterPoint NextEnterPoint { get; set; }

	public ItemsCollection GetSharedStash(SharedStashType type)
	{
		return type switch
		{
			SharedStashType.BESMARITES => Game.Instance.Player.SecondSharedStash, 
			SharedStashType.MEMORIES => Game.Instance.Player.ThirdSharedStash, 
			_ => Game.Instance.Player.SharedStash, 
		};
	}

	public void UpdateIsInCombat()
	{
		UpdateCharacterLists();
		bool flag = false;
		for (int i = 0; i < PartyAndPets.Count; i++)
		{
			if ((bool)PartyAndPets[i].Group.IsInCombat)
			{
				flag = true;
				break;
			}
		}
		if (IsInCombat != flag)
		{
			IsInCombat = flag;
			EventBus.RaiseEvent(delegate(IPartyCombatHandler h)
			{
				h.HandlePartyCombatStateChanged(IsInCombat);
			});
		}
	}

	[JsonConstructor]
	protected Player(JsonConstructorMark _)
		: base(_)
	{
	}

	public Player()
		: base("player_" + global::Pathfinding.Util.Guid.NewGuid().ToString(), isInGame: true)
	{
		m_QuestBook = new QuestBook();
		SharedStash = new ItemsCollection(this);
		SecondSharedStash = new ItemsCollection(this);
		ThirdSharedStash = new ItemsCollection(this);
		EtudesSystem = new EtudesSystem();
		Tutorial = new TutorialSystem();
		if (OwlcatModificationsManager.Instance.IsAnyModActive)
		{
			MarkModsUser();
		}
	}

	public void Initialize()
	{
		GameTime = BlueprintRoot.Instance.Calendar.GetStartTime();
		EnsureInventory();
		Corruption.TurnOn();
		Achievements.Activate();
		PSNObjects.Activate();
		AppliedPlayerUpgraders.AddRange(BlueprintRoot.Instance.PlayerUpgradeActions.Upgraders);
		IgnoredNotAppliedPlayerUpgraders.AddRange(BlueprintRoot.Instance.PlayerUpgradeActions.IgnoreUpgraders);
	}

	protected override void OnDispose()
	{
		Kingdom?.Dispose();
		m_QuestBook.Dispose();
		Inventory?.Dispose();
		CrossSceneState.Dispose();
		SharedStash.Dispose();
		SecondSharedStash.Dispose();
		ThirdSharedStash.Dispose();
		Achievements.Dispose();
		Tutorial.Dispose();
		EtudesSystem.Dispose();
		Corruption.TurnOff();
	}

	protected override EntityViewBase CreateViewForData()
	{
		return null;
	}

	protected override void OnPostLoad()
	{
		base.OnPostLoad();
		if (Tutorial == null)
		{
			Tutorial = new TutorialSystem();
		}
		m_QuestBook.PostLoad();
		CrossSceneState.PostLoad();
		Inventory = MainCharacter.Value.Inventory;
		Inventory.PostLoad();
		foreach (UnitEntityData allCrossSceneUnit in AllCrossSceneUnits)
		{
			if (allCrossSceneUnit.Inventory != Inventory)
			{
				allCrossSceneUnit.Inventory?.PostLoad();
			}
		}
		SharedStash.PostLoad();
		if (SecondSharedStash == null)
		{
			SecondSharedStash = new ItemsCollection(this);
		}
		SecondSharedStash.PostLoad();
		if (ThirdSharedStash == null)
		{
			ThirdSharedStash = new ItemsCollection(this);
		}
		ThirdSharedStash.PostLoad();
		MinDifficultyController.PostLoad();
		Kingdom?.PostLoad();
		Tutorial.PostLoad();
		EtudesSystem.PostLoad();
		ArmyLeadersManager.PostLoad();
		Corruption.TurnOn();
		foreach (GlobalMapState globalMap in m_GlobalMaps)
		{
			globalMap.PostLoad();
		}
		CameraRig cameraRig = Game.Instance.UI.GetCameraRig();
		if ((bool)cameraRig)
		{
			cameraRig.SavedPosition = m_CameraPos;
			cameraRig.SavedRotation = m_CameraRot;
		}
		Achievements.Activate();
		PSNObjects.Activate();
		if (StartPreset == null)
		{
			StartPreset = BlueprintRoot.Instance.NewGamePreset;
		}
		UISettingsRoot.Instance.UpdateOverrides();
		ApplyPostLoadFixes();
	}

	protected override void OnApplyPostLoadFixes()
	{
		Kingdom?.ApplyPostLoadFixes();
		m_QuestBook.ApplyPostLoadFixes();
		Tutorial.ApplyPostLoadFixes();
		EtudesSystem.ApplyPostLoadFixes();
		MainCharacter.Value.Body.EnsureActiveHands();
		foreach (ItemEntity item in Inventory.Items)
		{
			item.ApplyPostLoadFixes();
		}
		PartyCharacters.RemoveAll((UnitReference i) => i.Value == null || i.Value.IsPet);
		CrossSceneState.AllEntityData.OfType<DroppedLoot.EntityData>().ForEach(delegate(DroppedLoot.EntityData e)
		{
			e.MarkForDestroy();
		});
		m_ShouldFixPartyOnAreaLoaded = true;
		CrossSceneState.AllEntityData.OfType<UnitEntityData>().ForEach(delegate(UnitEntityData e)
		{
			e.Descriptor.FixInventoryOnPlayerPostLoad();
		});
		CrossSceneState.AllEntityData.OfType<UnitEntityData>().ForEach(delegate(UnitEntityData e)
		{
			UnitPartCompanion unitPartCompanion = e.Get<UnitPartCompanion>();
			if (unitPartCompanion != null && !e.IsPet)
			{
				unitPartCompanion.SetState(unitPartCompanion.State);
			}
		});
	}

	protected override void OnTurnOn()
	{
		base.OnTurnOn();
		Kingdom?.TurnOn();
		QuestBook.TurnOn();
		Inventory.TurnOn();
		SharedStash.TurnOn();
		SecondSharedStash.TurnOn();
		ThirdSharedStash.TurnOn();
		SharedVendorTables.TurnOn();
		Tutorial.TurnOn();
		EtudesSystem.TurnOn();
		Corruption.TurnOn();
	}

	protected override void OnTurnOff(Reason reason = Reason.Default)
	{
		base.OnTurnOff(reason);
		Tutorial.TurnOff();
		Kingdom?.TurnOff();
		QuestBook.TurnOff();
		Inventory?.TurnOff();
		SharedStash.TurnOff();
		SecondSharedStash.TurnOff();
		ThirdSharedStash.TurnOff();
		SharedVendorTables.TurnOff();
		if (reason != Reason.SaveGame)
		{
			Achievements.Deactivate();
		}
		EtudesSystem.TurnOff();
		Corruption.TurnOff();
	}

	public void ApplyUpgrades()
	{
		foreach (PlayerUpgradeAction upgradeAction in UpgradeActions)
		{
			try
			{
				upgradeAction.Apply();
			}
			catch (Exception ex)
			{
				PFLog.Default.Error($"Exception while applying upgrade action: {upgradeAction.Type} ({upgradeAction.Blueprint})");
				PFLog.Default.Exception(ex, null);
			}
		}
		UpgradeActions.Clear();
		if ((bool)BlueprintRoot.Instance.PlayerUpgradeActions)
		{
			BlueprintRoot.Instance.PlayerUpgradeActions.ApplyUpgrades();
		}
	}

	protected override void OnPreSave()
	{
		base.OnPreSave();
		Kingdom?.PreSave();
		m_QuestBook.PreSave();
		CrossSceneState.PreSave();
		Inventory.PreSave();
		SharedStash.PreSave();
		SecondSharedStash.PreSave();
		ThirdSharedStash.PreSave();
		MinDifficultyController.PreSave();
		SharedVendorTables.PreSave();
		Tutorial.PreSave();
		EtudesSystem.PreSave();
		CameraRig cameraRig = Game.Instance.UI.GetCameraRig();
		m_CameraPos = (cameraRig ? cameraRig.transform.position : Vector3.zero);
		m_CameraRot = (cameraRig ? cameraRig.transform.eulerAngles.y : 0f);
	}

	public void SetMainCharacter(UnitEntityData unit)
	{
		if (MainCharacter == unit)
		{
			return;
		}
		if (MainCharacter != null)
		{
			PartyCharacters.Remove(MainCharacter);
			if (IsClearMainCharacterData)
			{
				IsClearMainCharacterData = false;
				MainCharacter.Value.Dispose();
			}
		}
		if (unit != null)
		{
			PartyCharacters.Add(unit);
			unit.Ensure<UnitPartCompanion>().SetState(CompanionState.InParty);
			Inventory = unit.Inventory;
		}
		MainCharacter = unit;
		InvalidateCharacterLists();
	}

	public AreaEnterPoint MoveCharacters(BlueprintAreaEnterPoint enterPoint, bool moveFollowers, bool moveCamera)
	{
		AreaEnterPoint areaEnterPoint = AreaEnterPoint.FindAreaEnterPointOnScene(enterPoint);
		if (areaEnterPoint != null)
		{
			areaEnterPoint.PositionCharacters();
		}
		else
		{
			if (GlobalMapView.LastEntranceState != null)
			{
				BlueprintAreaEnterPoint blueprintAreaEnterPoint = GlobalMapView.LastEntranceState?.GetPointState(GlobalMapView.LastEntranceState.Player.Location)?.AreaEntrance;
				if (blueprintAreaEnterPoint != enterPoint && blueprintAreaEnterPoint.Area == enterPoint.Area)
				{
					areaEnterPoint = AreaEnterPoint.FindAreaEnterPointOnScene(blueprintAreaEnterPoint);
				}
			}
			if (areaEnterPoint == null)
			{
				if (Application.isPlaying)
				{
					PFLog.Default.Error($"Can't find enter point for {enterPoint.NameSafe()} ({enterPoint?.AssetGuid}) in area");
				}
				PartySpawnPlace partySpawnPlace = UnityEngine.Object.FindObjectsOfType<PartySpawnPlace>().FirstOrDefault();
				if (partySpawnPlace != null && MainCharacter != null)
				{
					MainCharacter.Value.View?.StopMoving();
					MainCharacter.Value.Position = partySpawnPlace.transform.position;
					MainCharacter.Value.Orientation = partySpawnPlace.transform.rotation.eulerAngles.y;
				}
			}
		}
		if (enterPoint.Area.IsGlobalMap && MainCharacter.Value?.RiderPart != null)
		{
			MainCharacter.Value.TempUnmount();
		}
		if (moveFollowers)
		{
			foreach (UnitEntityData item in Party)
			{
				UnitPartFollowedByUnits unitPartFollowedByUnits = item.Get<UnitPartFollowedByUnits>();
				if (unitPartFollowedByUnits == null)
				{
					continue;
				}
				foreach (KeyValuePair<UnitEntityData, FollowerAction> item2 in Game.Instance.FollowersFormationController.CalculateTeleportToLeaderDestinations(unitPartFollowedByUnits))
				{
					UnitEntityData key = item2.Key;
					FollowerAction value = item2.Value;
					key.View?.StopMoving();
					key.Position = value.Position;
					key.DesiredOrientation = value.Orientation;
				}
			}
		}
		if (moveCamera)
		{
			CameraRig cameraRig = Game.Instance.UI.GetCameraRig();
			if (areaEnterPoint.MoveCameraOnEnter && areaEnterPoint.CameraPositionTransform != null)
			{
				cameraRig.ScrollToImmediately(areaEnterPoint.CameraPositionTransform.position);
			}
			else
			{
				UnitPartCompanion unitPartCompanion = MainCharacter.Value.Get<UnitPartCompanion>();
				UnitEntityData unitEntityData = ((unitPartCompanion != null && unitPartCompanion.State == CompanionState.InPartyDetached) ? Party.FirstItem() : MainCharacter.Value);
				cameraRig.ScrollToImmediately(unitEntityData.Position);
			}
		}
		foreach (UnitEntityData item3 in Party)
		{
			foreach (Familiar familiar in item3.Familiars)
			{
				ObjectExtensions.Or(familiar, null)?.TeleportToMaster();
			}
		}
		EventBus.RaiseEvent(delegate(ITeleportHandler h)
		{
			h.HandlePartyTeleport(areaEnterPoint);
		});
		return areaEnterPoint;
	}

	public void GainPartyExperience(int gained, ExperienceGainStatistic.GainType statType = ExperienceGainStatistic.GainType.Quest)
	{
		BlueprintCampaignExperience component = Game.Instance.Player.Campaign.GetComponent<BlueprintCampaignExperience>();
		if (component != null && !component.Allow(statType))
		{
			return;
		}
		int num;
		if ((bool)SettingsRoot.Difficulty.OnlyActiveCompanionsReceiveExperience)
		{
			CountingGuard countingGuard = Game.Instance.LoadedAreaState?.Settings.CapitalPartyMode;
			num = ((countingGuard != null && (bool)countingGuard) ? 1 : 0);
		}
		else
		{
			num = 1;
		}
		bool flag = (byte)num != 0;
		IEnumerable<UnitEntityData> enumerable;
		if (!flag)
		{
			IEnumerable<UnitEntityData> party = Party;
			enumerable = party;
		}
		else
		{
			enumerable = AllCharacters.Where((UnitEntityData u) => u.Master == null);
		}
		IEnumerable<UnitEntityData> enumerable2 = enumerable;
		int num2 = gained * 6;
		int num3 = gained;
		if (!flag)
		{
			int num4 = enumerable2.Count((UnitEntityData u) => !u.Descriptor.State.IsFinallyDead);
			if (num4 < 1)
			{
				return;
			}
			num3 = num2 / num4;
		}
		HashSet<UnitEntityData> hashSet = new HashSet<UnitEntityData>();
		foreach (UnitEntityData item in enumerable2)
		{
			if ((flag || !item.Descriptor.State.IsFinallyDead) && !hashSet.Contains(item))
			{
				UnitEntityData pair = UnitPartDualCompanion.GetPair(item);
				if (!(pair != null) || !hashSet.Contains(pair))
				{
					hashSet.Add(item);
					item.Descriptor.Progression.GainExperience(num3, log: false, allowForPet: false, statType);
				}
			}
		}
		int eventExp = (flag ? gained : num3);
		if (eventExp > 0)
		{
			EventBus.RaiseEvent(delegate(IPartyGainExperienceHandler h)
			{
				h.HandlePartyGainExperience(eventExp);
			});
		}
	}

	public bool SpendMoney(long amount)
	{
		if (Money < amount)
		{
			return false;
		}
		Money -= amount;
		return true;
	}

	public void GainMoney(long amount)
	{
		Money += amount;
	}

	public void OnAreaLoaded()
	{
		if (Dialog.Scheduled != null && !Dialog.Scheduled.IsScheduled)
		{
			Game.Instance.DialogController.StartScheduledDialog();
		}
		if (m_ShouldFixPartyOnAreaLoaded)
		{
			m_ShouldFixPartyOnAreaLoaded = false;
			FixPartyAfterChange();
		}
		if (!Game.Instance.CurrentlyLoadedArea.IsGlobalMap)
		{
			MainCharacter.Value.RecoveryMount();
		}
	}

	public void AddCompanion(UnitEntityData value)
	{
		if (value.View != null)
		{
			value.View.HandleOccludedObjectDepthClipper(toggle: true);
		}
		if (value.IsPet)
		{
			PFLog.Default.ErrorWithReport("Can't add pet to PartyCharacters list");
			return;
		}
		value.Ensure<UnitPartCompanion>().SetState(CompanionState.InParty);
		PartyCharacters.Add(value);
		InvalidateCharacterLists();
		if (Campaign == null || !Campaign.MythicLevelsIsUniqueForEachCharacter)
		{
			value.Progression.AdvanceMythicExperience(MythicExperience);
		}
		BlueprintArea blueprintArea = Game.Instance?.LoadedAreaState?.Area.Blueprint;
		if (blueprintArea != null && blueprintArea.IsGlobalMap)
		{
			value.IsInGame = true;
		}
		EventBus.RaiseEvent(delegate(IPartyHandler h)
		{
			h.HandleCompanionActivated(value);
		});
	}

	public void RemoveEverybody()
	{
		foreach (UnitReference item in PartyCharacters.ToTempList())
		{
			RemoveCompanionInternal(item);
		}
		List<UnitEntityData> list = new List<UnitEntityData>();
		foreach (EntityDataBase allEntityDatum in CrossSceneState.AllEntityData)
		{
			UnitEntityData unitEntityData = allEntityDatum as UnitEntityData;
			if (!(unitEntityData == null))
			{
				list.Add(unitEntityData);
			}
		}
		foreach (UnitEntityData item2 in list)
		{
			CrossSceneState.AllEntityData.Remove(item2);
		}
		UpdateCharacterLists();
	}

	public void RemoveCompanion(UnitEntityData value, bool stayInGame = false)
	{
		if (MainCharacter == value)
		{
			PFLog.Default.Error("Trying to remove Main Character from party");
			return;
		}
		if (value.View != null)
		{
			value.View.HandleOccludedObjectDepthClipper();
		}
		RemoveCompanionInternal(value, stayInGame);
	}

	public void RemoveCompanions(bool stayInGame = false)
	{
		foreach (UnitEntityData item in m_ActiveCompanions.ToTempList())
		{
			if (!(item == MainCharacter))
			{
				RemoveCompanion(item, stayInGame);
			}
		}
	}

	private void RemoveCompanionInternal(UnitEntityData value, bool stayInGame = false)
	{
		if (PartyCharacters.Contains(value))
		{
			PartyCharacters.Remove(value);
		}
		else
		{
			foreach (UnitReference partyCharacter in PartyCharacters)
			{
				if (partyCharacter.Value.Pets.Contains(value))
				{
					partyCharacter.Value.Pets.Remove(value);
				}
			}
		}
		value.Ensure<UnitPartCompanion>().SetState(CompanionState.Remote);
		InvalidateCharacterLists();
		if (!stayInGame)
		{
			value.IsInGame = value.Get<UnitPartCompanion>()?.GetCurrentSpawner()?.ShouldShowUnit(value) == true;
			if ((bool)Game.Instance.UI.SelectionManager)
			{
				Game.Instance.UI.SelectionManager.UpdateSelectedUnits();
			}
		}
		EventBus.RaiseEvent(delegate(IPartyHandler h)
		{
			h.HandleCompanionRemoved(value, stayInGame);
		});
	}

	public void ReplaceCompanion(UnitEntityData remove, UnitEntityData add)
	{
		if (MainCharacter == remove)
		{
			PFLog.Default.Error("Trying to remove Main Character from party");
			return;
		}
		UnitPartCompanion unitPartCompanion = remove.Get<UnitPartCompanion>();
		if (unitPartCompanion == null || unitPartCompanion.State != CompanionState.InParty)
		{
			PFLog.Default.Error("Trying to remove non-active companion!");
			return;
		}
		UnitPartCompanion unitPartCompanion2 = add.Get<UnitPartCompanion>();
		if (unitPartCompanion2 == null || unitPartCompanion2.State != CompanionState.Remote)
		{
			PFLog.Default.Error("Trying to add companion that is not remote!");
			return;
		}
		ReplaceCompanionInList(remove, add, PartyCharacters);
		remove.Ensure<UnitPartCompanion>().SetState(CompanionState.Remote);
		add.Ensure<UnitPartCompanion>().SetState(CompanionState.InParty);
		InvalidateCharacterLists();
		if ((bool)Game.Instance.UI.SelectionManager)
		{
			Game.Instance.UI.SelectionManager.ReplaceSelection(remove, add);
		}
		EventBus.RaiseEvent(delegate(IPartyHandler h)
		{
			h.HandleCompanionRemoved(remove, stayInGame: false);
		});
		EventBus.RaiseEvent(delegate(IPartyHandler h)
		{
			h.HandleCompanionActivated(add);
		});
	}

	private static void ReplaceCompanionInList(UnitEntityData remove, UnitEntityData add, List<UnitReference> list)
	{
		int num = list.IndexOf(remove);
		if (num >= 0)
		{
			list[num] = add;
		}
	}

	public void DismissCompanion([NotNull] UnitEntityData value)
	{
		if (!value.CanDismissCompanion)
		{
			PFLog.Default.Error("Trying to remove story companion from party");
			return;
		}
		PartyCharacters.Remove(value);
		value.Ensure<UnitPartCompanion>().SetState(CompanionState.ExCompanion);
		InvalidateCharacterLists();
		value.IsInGame = false;
		for (int i = 0; i < Inventory.Items.Count; i++)
		{
			ItemEntity itemEntity = Inventory.Items[i];
			if (itemEntity.Owner == value.Descriptor && !itemEntity.HoldingSlot.RemoveItem())
			{
				PFLog.Default.Error("Unable to unequip item {0} while dismissing a custom companion: item will disappear!", itemEntity.Blueprint);
			}
		}
		value.MarkForDestroy();
		EventBus.RaiseEvent(delegate(IPartyHandler h)
		{
			h.HandleCompanionRemoved(value, stayInGame: false);
		});
	}

	public void DetachPartyMember(UnitEntityData unit)
	{
		if (unit.IsReallyInFactPet)
		{
			return;
		}
		if (!PartyCharacters.Contains(unit))
		{
			PFLog.Default.Error($"Unit {unit} is not in party or already detached");
			return;
		}
		if (PartyCharacters.Count < 2)
		{
			PFLog.Default.Error("Can't detach all party members");
			return;
		}
		PartyCharacters.Remove(unit);
		unit.Ensure<UnitPartCompanion>().SetState(CompanionState.InPartyDetached);
		foreach (EntityPartRef<UnitEntityData, UnitPartPet> pet in unit.Pets)
		{
			pet.Entity?.Ensure<UnitPartCompanion>().SetState(CompanionState.InPartyDetached);
		}
		InvalidateCharacterLists();
		Game.Instance.UI.SelectionManager.UpdateSelectedUnits();
		EventBus.RaiseEvent(delegate(IPartyHandler h)
		{
			h.HandleCompanionRemoved(unit, stayInGame: false);
		});
	}

	public void AttachPartyMember(UnitEntityData unit)
	{
		if (unit.IsReallyInFactPet)
		{
			return;
		}
		if (!unit.IsDetached)
		{
			PFLog.Default.Error($"Unit {unit} is not detached");
			return;
		}
		unit.Ensure<UnitPartCompanion>().SetState(CompanionState.InParty);
		foreach (EntityPartRef<UnitEntityData, UnitPartPet> pet in unit.Pets)
		{
			pet.Entity?.Ensure<UnitPartCompanion>().SetState(CompanionState.InParty);
		}
		PartyCharacters.Add(unit);
		InvalidateCharacterLists();
		EventBus.RaiseEvent(delegate(IPartyHandler h)
		{
			h.HandleAddCompanion(unit);
		});
	}

	public void SwapAttachedAndDetachedPartyMembers()
	{
		List<UnitEntityData> list = AllCrossSceneUnits.Where((UnitEntityData u) => !u.IsPet && u.IsDetached).ToTempList();
		if (list.Count < 1)
		{
			throw new Exception("Has no detached party members");
		}
		List<UnitReference> list2 = PartyCharacters.ToList();
		list.ForEach(delegate(UnitEntityData u)
		{
			AttachPartyMember(u);
		});
		list2.ForEach(delegate(UnitReference u)
		{
			DetachPartyMember(u);
		});
		InvalidateCharacterLists();
	}

	public UnitEntityData GetMainPartyUnit()
	{
		UnitEntityData value = MainCharacter.Value;
		if (value.IsDetached)
		{
			foreach (UnitEntityData item in Party)
			{
				if (!item.State.IsDead)
				{
					return item;
				}
			}
		}
		return value;
	}

	public void InvalidateCharacterLists()
	{
		m_CharacterListsValid = false;
	}

	public void UpdateCharacterLists()
	{
		if (m_CharacterListsValid)
		{
			return;
		}
		m_Party.Clear();
		m_ActiveCompanions.Clear();
		m_PartyAndPets.Clear();
		m_AllCharacters.Clear();
		m_RemoteCompanions.Clear();
		m_PartyAndPetsDetached.Clear();
		foreach (UnitReference item in PartyCharacters.ToTempList())
		{
			AddCharacterToLists(item.Value);
		}
		foreach (EntityDataBase allEntityDatum in CrossSceneState.AllEntityData)
		{
			UnitEntityData unitEntityData = allEntityDatum as UnitEntityData;
			if (!(unitEntityData == null))
			{
				AddCharacterToLists(unitEntityData);
			}
		}
		m_CharacterListsValid = true;
	}

	private void AddCharacterToLists(UnitEntityData unit)
	{
		if (unit == null)
		{
			PFLog.Default.Error("Unit is null! (maybe you load old save or do something from OnDispose)");
			return;
		}
		if (m_AllCharacters.Contains(unit))
		{
			return;
		}
		UnitEntityData unitEntityData = unit.Master ?? unit;
		CompanionState? obj = unitEntityData.Get<UnitPartCompanion>()?.State;
		bool flag = obj == CompanionState.InParty;
		bool num = obj == CompanionState.InPartyDetached;
		bool isPet = unit.IsPet;
		if (flag && !PartyCharacters.Contains(unitEntityData))
		{
			LogChannel.Default.Warning($"Unit {unitEntityData} in party, but not in party list. Fixing.");
			PartyCharacters.Add(unitEntityData);
		}
		if (!flag && PartyCharacters.Remove(unitEntityData))
		{
			LogChannel.Default.Warning($"Unit {unitEntityData} not in party, but in party list. Fixing.");
		}
		if (CapitalPartyMode)
		{
			flag = unitEntityData.IsMainCharacter && (!isPet || !IsControlledBySpawner(unit));
		}
		if (!isPet && flag)
		{
			m_Party.Add(unit);
		}
		if (!isPet && flag && unitEntityData != MainCharacter)
		{
			m_ActiveCompanions.Add(unit);
		}
		UnitPartCompanion unitPartCompanion = unitEntityData.Get<UnitPartCompanion>();
		if (unitPartCompanion == null || unitPartCompanion.State != CompanionState.Remote)
		{
			UnitPartCompanion unitPartCompanion2 = unitEntityData.Get<UnitPartCompanion>();
			if (unitPartCompanion2 == null || unitPartCompanion2.State != CompanionState.InParty || flag)
			{
				goto IL_01a0;
			}
		}
		m_RemoteCompanions.Add(unit);
		goto IL_01a0;
		IL_01a0:
		if (flag && base.IsInGame)
		{
			m_PartyAndPets.Add(unit);
		}
		if (num && base.IsInGame)
		{
			m_PartyAndPetsDetached.Add(unit);
		}
		m_AllCharacters.Add(unit);
		static bool IsControlledBySpawner(UnitEntityData u)
		{
			CompanionSpawner companionSpawner = u.Get<UnitPartCompanion>()?.GetCurrentSpawner();
			if ((bool)companionSpawner)
			{
				return u == companionSpawner.Data.SpawnedUnit;
			}
			return false;
		}
	}

	public IEnumerable<UnitEntityData> GetCharactersList(CharactersList type)
	{
		return type switch
		{
			CharactersList.ActiveUnits => PartyAndPets, 
			CharactersList.Everyone => AllCharacters, 
			CharactersList.AllDetachedUnits => AllCharacters.Where((UnitEntityData u) => u.IsDetached).ToTempList(), 
			CharactersList.DetachedPartyCharacters => AllCharacters.Where((UnitEntityData u) => !u.IsPet && u.IsDetached).ToTempList(), 
			CharactersList.PartyCharacters => Party, 
			_ => throw new ArgumentOutOfRangeException("type", type, null), 
		};
	}

	public List<UnitEntityData> GetPartyCharactersForGroupCommand(Vector3 approachPoint, bool skipNoIsInGame = false)
	{
		ObstacleAnalyzer.GetArea(approachPoint);
		return (from u in PartyAndPets
			where u.IsDirectlyControllable
			where u.Descriptor.State.CanMove
			where !u.Descriptor.State.HasCondition(UnitCondition.Paralyzed)
			where u.Parts.Get<UnitPartSaddled>() == null
			where u.IsInGame || !skipNoIsInGame
			select u).ToList();
	}

	public void FixPartyAfterChange(bool ignoreCapitalMode = false)
	{
		foreach (UnitEntityData item in AllCharacters.ToTempList())
		{
			UnitPartCompanion unitPartCompanion = item.Get<UnitPartCompanion>();
			if (unitPartCompanion == null || unitPartCompanion.State == CompanionState.None)
			{
				continue;
			}
			if (unitPartCompanion.State == CompanionState.ExCompanion)
			{
				if (!item.IsPlayerFaction && item.GroupId == "<directly-controllable-unit>")
				{
					item.GroupId = item.UniqueId;
				}
			}
			else
			{
				if (unitPartCompanion.IsIgnoreFixInGame || unitPartCompanion.Owner.IsPet)
				{
					continue;
				}
				FixPartyMember(item, unitPartCompanion.State, ignoreCapitalMode);
				foreach (EntityPartRef<UnitEntityData, UnitPartPet> pet in item.Pets)
				{
					UnitPartPet entityPart = pet.EntityPart;
					if (entityPart != null && !entityPart.ShouldBeHidden)
					{
						FixPartyMember(pet.Entity, unitPartCompanion.State, ignoreCapitalMode);
					}
				}
			}
		}
	}

	private void FixPartyMember(UnitEntityData unit, CompanionState companionState, bool ignoreCapitalMode)
	{
		if (unit == null)
		{
			return;
		}
		if ((!CapitalPartyMode || ignoreCapitalMode) && (!unit.IsInGame || unit.Suppressed || !AreaService.Instance.IsInMechanicBounds(unit.Position)) && companionState != CompanionState.Remote && companionState != CompanionState.InPartyDetached)
		{
			unit.IsInGame = true;
			Vector3 vector = Game.Instance.Player.MainCharacter.Value.Position;
			if ((bool)AstarPath.active)
			{
				FreePlaceSelector.PlaceSpawnPlaces(2, unit.View.Corpulence, vector);
				vector = FreePlaceSelector.GetRelaxedPosition(1, projectOnGround: true);
			}
			unit.Position = vector;
		}
		if (!unit.Faction.IsDirectlyControllable)
		{
			unit.Descriptor.SwitchFactions(BlueprintRoot.Instance.PlayerFaction);
		}
		if (unit.GroupId != "<directly-controllable-unit>")
		{
			unit.GroupId = "<directly-controllable-unit>";
		}
	}

	public Vector3 GetPartyCenter()
	{
		Vector3 zero = Vector3.zero;
		int num = 0;
		foreach (UnitEntityData partyAndPet in Game.Instance.Player.PartyAndPets)
		{
			if (!partyAndPet.IsHiddenBecauseDead)
			{
				zero += partyAndPet.Position;
				num++;
			}
		}
		if (num > 1)
		{
			zero /= (float)num;
		}
		return zero;
	}

	public void CreateCustomCompanion(Action<UnitEntityData> successCallback = null, int? xp = null, bool importable = false, bool lockInUi = false)
	{
		int targetExp = xp ?? BlueprintRoot.Instance.Progression.XPTable.GetBonus(GetCustomCompanionStartLevel());
		int mythicExperience = MainCharacter.Value.Progression.MythicExperience;
		BlueprintUnit unit = UnitHelper.CustomCompanion();
		UnitEntityData newCompanion = Game.Instance.CreateUnitVacuum(unit);
		newCompanion.Progression.AdvanceExperienceTo(targetExp, log: false);
		if (Campaign == null || !Campaign.MythicLevelsIsUniqueForEachCharacter)
		{
			newCompanion.Progression.AdvanceMythicExperience(mythicExperience, log: false);
		}
		newCompanion.AttachView(newCompanion.CreateView());
		if (importable)
		{
			newCompanion.Ensure<UnitPartImportableCompanion>();
		}
		LevelUpConfig.Create(newCompanion, LevelUpState.CharBuildMode.CharGen).SetOnCommit(OnCommit).SetOnStop(OnStop)
			.SetLockInUi(lockInUi)
			.SetAllowInterrupt(!DungeonController.IsDungeonCampaign)
			.OpenUI();
		void OnCommit(LevelUpController controller)
		{
			Game.Instance.ScheduleAction(delegate
			{
				successCallback?.Invoke(newCompanion);
			});
		}
		void OnStop(LevelUpController controller)
		{
			if (!controller.Committed)
			{
				if (newCompanion.IsInState)
				{
					newCompanion.MarkForDestroy();
				}
				else
				{
					newCompanion.Dispose();
				}
			}
		}
	}

	public void CreateImportedCompanion(List<LevelPlanData> plan, UnitPartDollData doll)
	{
		int characterLevel = MainCharacter.Value.Descriptor.Progression.CharacterLevel;
		int bonus = BlueprintRoot.Instance.Progression.XPTable.GetBonus(characterLevel);
		BlueprintUnit unit = UnitHelper.CustomCompanion();
		UnitEntityData unitEntityData = Game.Instance.CreateUnitVacuum(unit);
		unitEntityData.Descriptor.Progression.AdvanceExperienceTo(bonus, log: false);
		unitEntityData.Ensure<UnitPartImportableCompanion>().ImportedLevel = plan.Max((LevelPlanData p) => p.Level);
		foreach (LevelPlanData item in plan)
		{
			unitEntityData.Descriptor.Progression.AddLevelPlan(item);
		}
		LevelUpController levelUpController = LevelUpController.Start(unitEntityData, LevelUpState.CharBuildMode.CharGen);
		levelUpController.ApplyPlanAsFarAsPossible();
		levelUpController.Commit();
		foreach (LevelPlanData item2 in plan)
		{
			if (item2.Level > unitEntityData.Descriptor.Progression.CharacterLevel)
			{
				unitEntityData.Descriptor.Progression.AddLevelPlan(item2);
			}
		}
		doll?.CopyTo(unitEntityData);
		Transform parent = unitEntityData.View.transform.parent;
		Utils.EditorSafeDestroy(unitEntityData.View);
		unitEntityData.AttachToViewOnLoad(null);
		unitEntityData.View.transform.SetParent(parent);
		Game.Instance.Player.AddCompanion(unitEntityData);
		unitEntityData.IsInGame = true;
		Vector3 vector = Game.Instance.Player.MainCharacter.Value.Position;
		if ((bool)AstarPath.active)
		{
			FreePlaceSelector.PlaceSpawnPlaces(2, unitEntityData.View.Corpulence, vector);
			vector = FreePlaceSelector.GetRelaxedPosition(1, projectOnGround: true);
		}
		unitEntityData.Position = vector;
	}

	public int GetCustomCompanionStartLevel()
	{
		if ((bool)SettingsRoot.Difficulty.OnlyActiveCompanionsReceiveExperience)
		{
			return PartyLevel;
		}
		int experience = MainCharacter.Value.Progression.Experience;
		int[] bonuses = BlueprintRoot.Instance.Progression.XPTable.Bonuses;
		int result = 0;
		for (int i = 0; i < bonuses.Length && experience >= bonuses[i]; i++)
		{
			result = i;
		}
		return result;
	}

	public int GetCustomCompanionCost()
	{
		int customCompanionStartLevel = GetCustomCompanionStartLevel();
		return BlueprintRoot.Instance.CustomCompanionBaseCost * customCompanionStartLevel * customCompanionStartLevel;
	}

	public void RespecCompanion(UnitEntityData unit, Action successCallback = null)
	{
		unit.Respec(successCallback);
	}

	public int GetRespecCost()
	{
		if (RespecsUsed >= 3)
		{
			return (RespecsUsed - 2) * 10000;
		}
		return 0;
	}

	public void SaveInventoryAsInitial()
	{
		InitialInventory = new HashSet<BlueprintItem>();
		foreach (ItemEntity item in Inventory.Items)
		{
			InitialInventory.Add(item.Blueprint);
		}
	}

	public ItemsCollection EnsureInventory()
	{
		if (Inventory == null)
		{
			Inventory = new ItemsCollection(this);
			EventBus.Subscribe(Inventory);
		}
		return Inventory;
	}

	public void GameOver(GameOverReasonType reason)
	{
		if (reason != GameOverReasonType.Won)
		{
			GameOverReason = reason;
			Game.Instance.StartMode(GameModeType.GameOver);
		}
		EventBus.RaiseEvent(delegate(IGameOverHandler h)
		{
			h.HandleGameOver(reason);
		});
		EventBus.RaiseEvent(delegate(IGameOverForAchievementsHandler h)
		{
			h.HandleGameOverForAchievements(reason);
		});
	}

	public void UnGameOver()
	{
		if (Game.Instance.CurrentMode == GameModeType.GameOver)
		{
			GameOverReason = null;
			Game.Instance.StopMode(GameModeType.GameOver);
		}
	}

	public void ReInitPartyCharacters(List<UnitReference> newCompanions)
	{
		foreach (UnitReference item in PartyCharacters.ToList())
		{
			if (!newCompanions.Contains(item))
			{
				RemoveCompanionInternal(item.Value);
			}
		}
		foreach (UnitReference newCompanion in newCompanions)
		{
			if (!PartyCharacters.Contains(newCompanion))
			{
				AddCompanion(newCompanion);
			}
		}
		PartyCharacters.Clear();
		foreach (UnitReference newCompanion2 in newCompanions)
		{
			PartyCharacters.Add(newCompanion2);
		}
		Camping.CleanupRoles();
	}

	public GlobalMapState GetGlobalMap(BlueprintGlobalMap blueprint)
	{
		GlobalMapState globalMapState = m_GlobalMaps.FirstItem((GlobalMapState i) => i.Blueprint == blueprint);
		if (globalMapState == null)
		{
			globalMapState = new GlobalMapState(blueprint);
			m_GlobalMaps.Add(globalMapState);
		}
		return globalMapState;
	}

	public GlobalMapState GetGlobalMapWithArmies()
	{
		return Game.Instance.Player.GetGlobalMap(BlueprintRoot.Instance.ArmyRoot.SummonArmiesMap);
	}

	public void ResetTradersLimit()
	{
		foreach (GlobalMapState globalMap in m_GlobalMaps)
		{
			globalMap.Player?.ResetTraderLimit();
		}
	}

	public void GainMythicExperience(int experience)
	{
		if (experience < 1)
		{
			return;
		}
		int target = MythicExperience + experience;
		foreach (UnitEntityData allCharacter in AllCharacters)
		{
			allCharacter.Progression.AdvanceMythicExperience(target);
		}
	}

	public void AdvanceMythicExperience(int experience)
	{
		if (experience < 1)
		{
			return;
		}
		int target = Math.Max(MythicExperience, experience);
		foreach (UnitEntityData allCharacter in AllCharacters)
		{
			allCharacter.Progression.AdvanceMythicExperience(target);
		}
	}

	public bool IsTurnBasedModeOn()
	{
		if (IsTurnBasedStateMandatory)
		{
			return MandatoryTurnBasedModeState;
		}
		return SettingsRoot.Game.TurnBased.EnableTurnBasedMode;
	}

	public void SetChapter(int chapter)
	{
		Chapter = chapter;
		ChapterStartDate = Game.Instance.TimeController.GameTime;
		EventBus.RaiseEvent(delegate(IChapterChangeHandler h)
		{
			h.HandleChapterChanged();
		});
	}

	public void MarkModsUser()
	{
		ModsUser = true;
	}

	public string GetNewUniqueId()
	{
		string text;
		do
		{
			text = m_MaxGameUniqueId++.ToString("X");
		}
		while (EntityService.Instance.GetEntity(text) != null);
		return text;
	}

	public void AddAdditionalTexture(string name, Texture2D tex)
	{
		if (!m_AdditionalTexNames.Contains(name))
		{
			m_AdditionalTexNames.Add(name);
		}
		AreaDataStash.SaveTexture(name, tex);
	}

	public bool TryGetTexture(string name, out Texture2D tex)
	{
		if (!Game.Instance.Player.AdditionalTexNames.Contains(name))
		{
			tex = null;
			return false;
		}
		tex = AreaDataStash.ReadTexture(name);
		return true;
	}
}
