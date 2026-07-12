using System;

namespace Kodoku.Items;

/// <summary>
/// Représentation minimale d'un exemplaire d'item dans une scène : relie une
/// <see cref="ItemDefinition"/> à une <see cref="ItemInstance"/> runtime pour un premier
/// test local. Ne gère ni ramassage, ni interaction, ni inventaire — voir
/// docs/architecture/ITEM_ARCHITECTURE.md.
///
/// Autorité réseau : la réplication de <see cref="Instance"/> n'est pas implémentée. Sur un
/// GameObject networké, seul le côté qui simule réellement cet objet (<see cref="IsProxy"/>
/// == false — propriétaire, ou host pour un objet non possédé, selon la sémantique confirmée
/// de <c>NetworkAccessor.IsProxy</c>) crée une instance locale ; un proxy reste volontairement
/// non initialisé plutôt que de générer un <see cref="ItemInstance.InstanceId"/> divergent.
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

	protected override void OnStart()
	{
		// GameObject.Network.Active == false : objet purement local, création directe sûre.
		// Networké + proxy : l'autorité réelle (propriétaire ou host) n'a pas encore répliqué
		// d'instance vers nous — ne pas en inventer une localement (voir résumé de classe).
		if ( GameObject.Network.Active && IsProxy )
		{
			Log.Warning( $"[WorldItem] '{GameObject.Name}' is a network proxy — no replicated ItemInstance exists yet (replication not implemented), leaving uninitialized." );
			return;
		}

		TryInitializeNew();
	}

	/// <summary>Crée une nouvelle <see cref="ItemInstance"/> à partir de <see cref="Definition"/>/<see cref="InitialQuantity"/>.</summary>
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
		LogInitialized();
		return true;
	}

	/// <summary>
	/// Initialise ce composant depuis une <see cref="ItemInstance"/> déjà existante (future
	/// restauration/réplication/spawn depuis un inventaire — non implémenté ici, seul ce
	/// point d'entrée est préparé).
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
		LogInitialized();
		return true;
	}

	void LogInitialized()
	{
		Log.Info( $"[WorldItem] Initialized\nGameObject: {GameObject.Name}\nItemId: {Instance.Definition.ItemId}\nInstanceId: {Instance.InstanceId}\nQuantity: {Instance.Quantity}\nMaxStack: {Instance.Definition.MaxStack}" );
	}
}
