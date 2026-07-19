namespace Kodoku.World.Containers;

/// <summary>
/// Résultat explicite d'une opération de session de <see cref="WorldContainerComponent"/>
/// (ouverture, fermeture, resynchronisation) — jamais un simple booléen, même pattern que
/// <see cref="Kodoku.Items.Interaction.PickupResult"/>/<see cref="Kodoku.Player.Inventory.DropResult"/>.
/// </summary>
public readonly record struct WorldContainerOperationResult
{
	public bool Success { get; }

	public WorldContainerFailureReason FailureReason { get; }

	WorldContainerOperationResult( bool success, WorldContainerFailureReason reason )
	{
		Success = success;
		FailureReason = reason;
	}

	public static WorldContainerOperationResult Ok() => new( true, WorldContainerFailureReason.None );

	public static WorldContainerOperationResult Fail( WorldContainerFailureReason reason ) => new( false, reason );
}
