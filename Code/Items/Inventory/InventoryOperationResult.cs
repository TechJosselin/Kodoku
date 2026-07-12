namespace Kodoku.Items.Inventory;

/// <summary>
/// Résultat explicite d'une opération de <see cref="InventoryContainer"/> pouvant échouer.
/// Jamais un simple booléen : <see cref="FailureReason"/> précise la cause d'un échec,
/// <see cref="Placement"/> porte l'état final réel en cas de succès (null sinon).
/// </summary>
public readonly record struct InventoryOperationResult
{
	public bool Success { get; }

	public InventoryFailureReason FailureReason { get; }

	public InventoryPlacement Placement { get; }

	InventoryOperationResult( bool success, InventoryFailureReason failureReason, InventoryPlacement placement )
	{
		Success = success;
		FailureReason = failureReason;
		Placement = placement;
	}

	public static InventoryOperationResult Ok( InventoryPlacement placement ) => new( true, InventoryFailureReason.None, placement );

	public static InventoryOperationResult Fail( InventoryFailureReason reason ) => new( false, reason, null );
}
