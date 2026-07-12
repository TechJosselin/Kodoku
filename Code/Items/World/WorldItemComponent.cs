using System;

namespace Kodoku.Items;

/// <summary>
/// Représentation minimale d'un exemplaire d'item dans une scène : relie une
/// <see cref="ItemDefinition"/> à une <see cref="ItemInstance"/> runtime. Ne gère ni
/// ramassage, ni interaction, ni inventaire — voir docs/architecture/ITEM_ARCHITECTURE.md.
///
/// Autorité réseau : le host est l'unique créateur d'une nouvelle <see cref="ItemInstance"/>
/// sur un GameObject networké (<see cref="TryInitializeAuthoritativeNew"/>), cohérent avec
/// ADR-0002. L'autorité est déterminée via <see cref="Sandbox.Networking.IsHost"/> — pas via
/// <c>IsProxy</c>, dont le timing juste après un spawn est un risque documenté (voir
/// MULTIPLAYER_ARCHITECTURE.md, section Ownership, et le vécu de l'ancien projet). L'état
/// minimal (<see cref="NetworkInstanceId"/>/<see cref="NetworkItemId"/>/
/// <see cref="NetworkQuantity"/>) est répliqué host→clients ; un client non-host ne crée
/// jamais d'<see cref="ItemInstance"/> lui-même, il restaure celle du host dès que l'état
/// synchronisé est complet (<see cref="TryRestoreFromNetworkState"/>).
/// </summary>
public sealed class WorldItemComponent : Component
{
	[Group( "Configuration" )]
	[Property]
	public ItemDefinition Definition { get; set; }

	[Group( "Configuration" )]
	[Range( 1, 999 )]
	[Property]
	public int InitialQuantity { get; set; } = 1;

	public ItemInstance Instance { get; private set; }

	[Group( "Runtime" )]
	[ReadOnly]
	[Property]
	public bool IsInitialized => Instance is not null;

	[Group( "Runtime" )]
	[ReadOnly]
	[Property]
	public string RuntimeInstanceId => Instance?.InstanceId.ToString() ?? "";

	[Group( "Runtime" )]
	[ReadOnly]
	[Property]
	public string RuntimeItemId => Instance?.Definition.ItemId ?? "";

	[Group( "Runtime" )]
	[ReadOnly]
	[Property]
	public int RuntimeQuantity => Instance?.Quantity ?? 0;

	/// <summary>
	/// État réseau autoritaire, renseigné uniquement par le host (<see cref="TryInitializeAuthoritativeNew"/>).
	/// <see cref="Guid"/> n'a pas de précédent confirmé comme type supporté par <c>[Sync]</c>
	/// dans ce projet ni dans le code moteur inspecté — représentation en chaîne canonique
	/// (<c>Guid.ToString()</c>), reconvertie avec validation côté réception plutôt que de
	/// supposer un support natif non vérifié.
	/// </summary>
	[Sync( SyncFlags.FromHost )]
	[Change( nameof( OnNetworkInstanceIdChanged ) )]
	public string NetworkInstanceId { get; set; } = "";

	[Sync( SyncFlags.FromHost )]
	[Change( nameof( OnNetworkItemIdChanged ) )]
	public string NetworkItemId { get; set; } = "";

	[Sync( SyncFlags.FromHost )]
	[Change( nameof( OnNetworkQuantityChanged ) )]
	public int NetworkQuantity { get; set; } = 0;

	protected override void OnStart()
	{
		// Un appelant externe (spawn programmatique) peut déjà avoir initialisé ce composant
		// avant que OnStart ne s'exécute (différé "avant le premier Update" — voir
		// docs/official/sbox/components/component-lifecycle.md) : rien à refaire.
		if ( IsInitialized )
			return;

		if ( GameObject.Network.Active && !Networking.IsHost )
		{
			// Attente normale d'un proxy réseau — pas une erreur, aucun warning par item
			// (voir docs/architecture/ITEM_ARCHITECTURE.md).
			TryRestoreFromNetworkState();
			return;
		}

		TryInitializeAuthoritativeNew();
	}

	/// <summary>
	/// Chemin de création unique et idempotent. Sur un GameObject non networké, crée
	/// simplement une <see cref="ItemInstance"/> locale. Sur un GameObject networké, seul le
	/// host peut l'appeler avec effet : il crée l'instance puis renseigne l'état réseau
	/// autoritaire (<see cref="NetworkInstanceId"/>/<see cref="NetworkItemId"/>/
	/// <see cref="NetworkQuantity"/>) pour que les autres clients restaurent la même instance.
	/// </summary>
	public bool TryInitializeAuthoritativeNew()
	{
		if ( GameObject.Network.Active && !Networking.IsHost )
		{
			Log.Warning( $"[WorldItem] '{GameObject.Name}': TryInitializeAuthoritativeNew() called on a non-host client — ignored." );
			return false;
		}

		if ( !TryInitializeNew() )
			return false;

		if ( GameObject.Network.Active )
		{
			NetworkInstanceId = Instance.InstanceId.ToString();
			NetworkItemId = Instance.Definition.ItemId;
			NetworkQuantity = Instance.Quantity;
		}
		else
		{
			Log.Info( $"[WorldItem][Initialized]\nItemId={Instance.Definition.ItemId}\nInstanceId={Instance.InstanceId}\nQuantity={Instance.Quantity}" );
		}

		return true;
	}

	/// <summary>Crée une nouvelle <see cref="ItemInstance"/> à partir de <see cref="Definition"/>/<see cref="InitialQuantity"/>, sans toucher l'état réseau.</summary>
	public bool TryInitializeNew()
	{
		if ( IsInitialized )
		{
			Log.Warning( $"[WorldItem] '{GameObject.Name}' is already initialized — ignoring TryInitializeNew()." );
			return false;
		}

		if ( Definition is null )
		{
			Log.Warning( $"[WorldItem] '{GameObject.Name}' has no Definition assigned — cannot initialize." );
			return false;
		}

		if ( string.IsNullOrEmpty( Definition.ItemId ) )
		{
			Log.Warning( $"[WorldItem] '{GameObject.Name}': Definition.ItemId is empty — cannot initialize." );
			return false;
		}

		if ( InitialQuantity < 1 || InitialQuantity > Definition.MaxStack )
		{
			Log.Warning( $"[WorldItem] '{GameObject.Name}': InitialQuantity ({InitialQuantity}) is out of range for '{Definition.ItemId}' (MaxStack = {Definition.MaxStack}) — cannot initialize." );
			return false;
		}

		Instance = ItemInstance.CreateNew( Definition, InitialQuantity );
		return true;
	}

	/// <summary>
	/// Initialise ce composant depuis une <see cref="ItemInstance"/> déjà existante (restauration
	/// réseau via <see cref="TryRestoreFromNetworkState"/>, et future sauvegarde/spawn depuis un
	/// inventaire — non implémenté ici, seul ce point d'entrée est préparé).
	/// </summary>
	public bool TryInitializeFromInstance( ItemInstance instance )
	{
		if ( IsInitialized )
		{
			Log.Warning( $"[WorldItem] '{GameObject.Name}' is already initialized — ignoring TryInitializeFromInstance()." );
			return false;
		}

		if ( instance is null )
		{
			Log.Warning( $"[WorldItem] '{GameObject.Name}': TryInitializeFromInstance called with a null instance." );
			return false;
		}

		if ( instance.Definition is null || string.IsNullOrEmpty( instance.Definition.ItemId ) )
		{
			Log.Warning( $"[WorldItem] '{GameObject.Name}': instance has no valid Definition — cannot initialize." );
			return false;
		}

		if ( Definition is not null && Definition != instance.Definition )
		{
			Log.Warning( $"[WorldItem] '{GameObject.Name}': configured Definition ('{Definition.ItemId}') does not match instance Definition ('{instance.Definition.ItemId}') — refusing to initialize." );
			return false;
		}

		Definition = instance.Definition;
		Instance = instance;
		return true;
	}

	/// <summary>
	/// Tentative idempotente de restauration depuis l'état réseau reçu. Ne crée jamais
	/// d'<see cref="ItemInstance.InstanceId"/> : n'agit que si l'état est complet, valide, et
	/// cohérent avec <see cref="Definition"/>. Appelée depuis <see cref="OnStart"/> (au cas où
	/// l'état serait déjà arrivé) et depuis chaque callback <c>[Change]</c> des propriétés
	/// réseau (late arrival — voir docs/architecture/ITEM_ARCHITECTURE.md).
	/// </summary>
	void TryRestoreFromNetworkState()
	{
		if ( IsInitialized )
			return;

		if ( string.IsNullOrEmpty( NetworkInstanceId ) || string.IsNullOrEmpty( NetworkItemId ) || NetworkQuantity <= 0 )
			return; // état pas encore complètement arrivé — pas une erreur, pas de log.

		if ( !Guid.TryParse( NetworkInstanceId, out var instanceId ) || instanceId == Guid.Empty )
		{
			Log.Error( $"[WorldItem][Joiner][RestoreFailed] '{GameObject.Name}': NetworkInstanceId '{NetworkInstanceId}' is not a valid Guid." );
			return;
		}

		if ( Definition is null )
		{
			Log.Error( $"[WorldItem][Joiner][RestoreFailed] '{GameObject.Name}': no local Definition configured to validate against NetworkItemId '{NetworkItemId}'." );
			return;
		}

		if ( Definition.ItemId != NetworkItemId )
		{
			Log.Error( $"[WorldItem][Joiner][RestoreFailed] '{GameObject.Name}': Definition.ItemId ('{Definition.ItemId}') does not match NetworkItemId ('{NetworkItemId}') — refusing to restore." );
			return;
		}

		ItemInstance restored;
		try
		{
			restored = ItemInstance.Restore( instanceId, Definition, NetworkQuantity );
		}
		catch ( ArgumentException e )
		{
			Log.Error( $"[WorldItem][Joiner][RestoreFailed] '{GameObject.Name}': {e.Message}" );
			return;
		}

		if ( !TryInitializeFromInstance( restored ) )
			return;
	}

	void OnNetworkInstanceIdChanged( string oldValue, string newValue ) => TryRestoreFromNetworkState();
	void OnNetworkItemIdChanged( string oldValue, string newValue ) => TryRestoreFromNetworkState();
	void OnNetworkQuantityChanged( int oldValue, int newValue ) => TryRestoreFromNetworkState();
}
