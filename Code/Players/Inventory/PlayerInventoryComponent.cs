using Kodoku.Items.Inventory;

namespace Kodoku.Player.Inventory;

/// <summary>
/// Inventaire réseau minimal d'un pawn Kodoku — porte un <see cref="InventoryContainer"/>
/// host-authoritative (cohérent avec ADR-0002, même choix que <see cref="Vitals.PlayerVitalsComponent"/>).
/// <see cref="Container"/> n'existe que côté host (voir <see cref="OnStart"/>) : aucune réplication
/// de son contenu vers les clients dans ce jalon — un client, y compris le propriétaire de ce pawn,
/// n'a aujourd'hui aucun moyen de lire son propre inventaire (pas d'UI, pas de snapshot réseau).
/// Ne pas présenter ce composant comme un inventaire réseau complet — voir
/// docs/architecture/ITEM_ARCHITECTURE.md, section « PlayerInventoryComponent (V1 minimale) ».
/// </summary>
public sealed class PlayerInventoryComponent : Component
{
	[Group( "Configuration" )]
	[Property]
	public int Width { get; set; } = 6;

	[Group( "Configuration" )]
	[Property]
	public int Height { get; set; } = 6;

	/// <summary>
	/// Null sur toute instance qui n'est pas celle du host (voir commentaire de classe). Un appelant
	/// doit toujours vérifier <see cref="Sandbox.Networking.IsHost"/> avant de muter ce conteneur —
	/// jamais exposé pour une mutation directe depuis un client.
	/// </summary>
	public InventoryContainer Container { get; private set; }

	/// <summary>
	/// Nombre d'objets actuellement placés — lecture seule, pour inspection en éditeur pendant
	/// l'exécution. Vaut toujours 0 sur une instance non-host (voir <see cref="Container"/>) : ne
	/// reflète le contenu réel que dans l'inspecteur du host, jamais celui d'un client distant.
	/// </summary>
	[Group( "Runtime" )]
	[ReadOnly]
	[Property]
	public int Count => Container?.Count ?? 0;

	/// <summary>Poids total actuellement porté — mêmes réserves que <see cref="Count"/>.</summary>
	[Group( "Runtime" )]
	[ReadOnly]
	[Property]
	public float CurrentWeight => Container?.CurrentWeight ?? 0f;

	/// <summary>
	/// Résumé lisible du contenu (ex. « Water Bottle x2 »), pour voir en un coup d'œil ce que porte
	/// ce pawn depuis l'inspecteur du host, sans UI dédiée. Mêmes réserves que <see cref="Count"/>.
	/// </summary>
	[Group( "Runtime" )]
	[ReadOnly]
	[Property]
	public string Contents => Container is null
		? ""
		: string.Join( ", ", Container.Placements.Select( p => $"{p.Item.Definition.DisplayName} x{p.Item.Quantity}" ) );

	protected override void OnStart()
	{
		// Même choix que WorldItemComponent/LootSpawnPointComponent : Networking.IsHost, pas IsProxy
		// (timing de IsProxy juste après spawn documenté comme risque — voir MULTIPLAYER_ARCHITECTURE.md).
		if ( !Networking.IsHost )
			return;

		Container = new InventoryContainer( Width, Height );
	}
}
