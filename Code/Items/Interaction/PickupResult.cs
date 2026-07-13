namespace Kodoku.Items.Interaction;

/// <summary>
/// Résultat explicite d'une tentative de pickup — jamais un simple booléen, même pattern que
/// <see cref="Kodoku.Items.Inventory.InventoryOperationResult"/>. <see cref="FailureReason"/>
/// précise la cause d'un échec ; en cas de succès elle vaut <see cref="PickupFailureReason.None"/>.
/// </summary>
public readonly record struct PickupResult
{
	public bool Success { get; }

	public PickupFailureReason FailureReason { get; }

	PickupResult( bool success, PickupFailureReason reason )
	{
		Success = success;
		FailureReason = reason;
	}

	public static PickupResult Ok() => new( true, PickupFailureReason.None );

	public static PickupResult Fail( PickupFailureReason reason ) => new( false, reason );
}
