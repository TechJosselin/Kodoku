namespace Kodoku.Items;

/// <summary>
/// Marqueur de conception de niveau : décide, uniquement côté host, si un exemplaire d'
/// <see cref="ItemDefinition"/> doit apparaître à cet emplacement. Ne porte aucune autorité
/// réseau propre et ne crée jamais lui-même d'<see cref="ItemInstance"/> — il clone le prefab
/// résolu depuis <see cref="ItemDefinition.WorldPrefabOverride"/> et laisse le
/// <see cref="WorldItemComponent"/> qu'il contient gérer la création autoritaire, la
/// réplication et la restauration côté joiners/late joiners (voir
/// docs/architecture/ITEM_ARCHITECTURE.md). Un <see cref="LootSpawnPointComponent"/> n'est pas
/// networké lui-même : seule l'autorité de session (<see cref="Sandbox.Networking.IsHost"/>)
/// détermine qui a le droit de l'évaluer.
/// </summary>
[Icon( "casino" )]
public sealed class LootSpawnPointComponent : Component
{
	[Group( "Configuration" )]
	[Property]
	public ItemDefinition Item { get; set; }

	[Group( "Configuration" )]
	[Range( 0f, 1f )]
	[Property]
	public float SpawnChance { get; set; } = 1f;

	[Group( "Configuration" )]
	[Range( 1, 999 )]
	[Property]
	public int Quantity { get; set; } = 1;

	[Group( "Runtime" )]
	[ReadOnly]
	[Property]
	public bool HasEvaluated { get; private set; }

	[Group( "Runtime" )]
	[ReadOnly]
	[Property]
	public bool HasSpawned { get; private set; }

	protected override void OnStart()
	{
		if ( HasEvaluated )
			return;

		// Un joiner ne fait jamais de tirage ni de clone — seul le host évalue les
		// LootSpawnPoint. Pas de log ici : ce non-comportement est normal sur chaque client,
		// pas une erreur (voir docs/architecture/ITEM_ARCHITECTURE.md).
		if ( !Networking.IsHost )
			return;

		TrySpawn();
	}

	/// <summary>
	/// Évaluation unique et idempotente du point : valide la configuration, effectue le
	/// tirage de <see cref="SpawnChance"/> (host uniquement), puis clone/network-spawn le
	/// prefab résolu depuis <see cref="ItemDefinition.WorldPrefabOverride"/> si le tirage
	/// réussit. Ne crée jamais d'<see cref="ItemInstance"/> directement : c'est le
	/// <see cref="WorldItemComponent"/> du prefab cloné qui s'en charge, via
	/// <see cref="WorldItemComponent.TryInitializeAuthoritativeNew"/> — même flux que le
	/// spawner debug déjà validé pour le networking des World Items.
	/// </summary>
	public bool TrySpawn()
	{
		if ( HasEvaluated )
		{
			Log.Warning( $"[LootSpawnPoint] '{GameObject.Name}' already evaluated — ignoring TrySpawn()." );
			return false;
		}

		if ( !Networking.IsHost )
		{
			Log.Warning( $"[LootSpawnPoint] '{GameObject.Name}': TrySpawn() called on a non-host client — ignored." );
			return false;
		}

		HasEvaluated = true;

		if ( Item is null )
		{
			Log.Warning( $"[LootSpawnPoint] '{GameObject.Name}' has no Item assigned — cannot spawn." );
			return false;
		}

		if ( string.IsNullOrEmpty( Item.ItemId ) )
		{
			Log.Warning( $"[LootSpawnPoint] '{GameObject.Name}': Item.ItemId is empty — cannot spawn." );
			return false;
		}

		if ( Item.WorldPrefabOverride is null )
		{
			Log.Warning( $"[LootSpawnPoint] '{GameObject.Name}': Item '{Item.ItemId}' has no WorldPrefabOverride — cannot spawn." );
			return false;
		}

		if ( Quantity < 1 || Quantity > Item.MaxStack )
		{
			Log.Warning( $"[LootSpawnPoint] '{GameObject.Name}': Quantity ({Quantity}) is out of range for '{Item.ItemId}' (MaxStack = {Item.MaxStack}) — cannot spawn." );
			return false;
		}

		if ( SpawnChance < 0f || SpawnChance > 1f )
		{
			Log.Warning( $"[LootSpawnPoint] '{GameObject.Name}': SpawnChance ({SpawnChance}) must be between 0 and 1 — cannot spawn." );
			return false;
		}

		if ( Game.Random.NextSingle() >= SpawnChance )
			return false;

		var spawned = Sandbox.GameObject.Clone( Item.WorldPrefabOverride, GameObject.WorldTransform );
		if ( spawned is null )
		{
			Log.Warning( $"[LootSpawnPoint] '{GameObject.Name}': GameObject.Clone returned null — spawn aborted." );
			return false;
		}

		var worldItem = spawned.Components.Get<WorldItemComponent>();
		if ( worldItem is null )
		{
			Log.Warning( $"[LootSpawnPoint] '{GameObject.Name}': cloned '{spawned.Name}' has no WorldItemComponent — destroying." );
			spawned.Destroy();
			return false;
		}

		// Doit être assigné avant NetworkSpawn()/TryInitializeAuthoritativeNew() : une fois
		// l'ItemInstance créée, InitialQuantity n'est plus lu (voir docs/architecture/ITEM_ARCHITECTURE.md).
		worldItem.InitialQuantity = Quantity;

		if ( !spawned.NetworkSpawn() )
		{
			Log.Warning( $"[LootSpawnPoint] '{GameObject.Name}': NetworkSpawn() failed for '{spawned.Name}' — destroying local copy." );
			spawned.Destroy();
			return false;
		}

		if ( !worldItem.TryInitializeAuthoritativeNew() )
		{
			Log.Warning( $"[LootSpawnPoint] '{GameObject.Name}': TryInitializeAuthoritativeNew() failed for '{spawned.Name}'." );
			return false;
		}

		HasSpawned = true;
		return true;
	}

	protected override void DrawGizmos()
	{
		Gizmo.Draw.Color = Color.Yellow;
		Gizmo.Draw.LineSphere( new Sphere( Vector3.Zero, 8f ) );
		Gizmo.Draw.WorldText( GameObject.Name, new Transform( Vector3.Up * 16f ) );
	}
}
