namespace Kodoku.World.Containers;

/// <summary>
/// Sens d'un transfert whole-item entre un <see cref="WorldContainerComponent"/> et l'inventaire
/// canonique d'un pawn (<see cref="Kodoku.Player.Inventory.PlayerInventoryComponent"/>). Ne porte
/// aucune sémantique de succès/échec — uniquement la direction, portée séparément par
/// <see cref="WorldContainerTransferResult"/>.
/// </summary>
public enum WorldContainerTransferDirection
{
	/// <summary>Conteneur monde -&gt; joueur.</summary>
	Take,

	/// <summary>Joueur -&gt; conteneur monde.</summary>
	Store,
}
