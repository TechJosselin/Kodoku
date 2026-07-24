namespace Kodoku.Items.Inventory;

/// <summary>
/// Raison d'échec explicite d'une opération de <see cref="InventoryStackTransactions"/>. Ne
/// duplique jamais une raison déjà exprimée par <see cref="InventoryFailureReason"/> — un échec
/// d'origine spatiale (hors limites, chevauchement, rotation interdite, plus de place) reste
/// encapsulé sous <see cref="DestinationInvalid"/>, avec la raison d'origine portée par
/// <see cref="StackTransactionResult.SpatialFailureReason"/>. Ne porte aucune raison réseau
/// (fraîcheur, ownership, portée) — ces raisons appartiennent à un futur jalon réseau, voir
/// docs/architecture/INVENTORY_STACK_ARCHITECTURE.md.
/// </summary>
public enum StackTransactionFailureReason
{
	None,
	InvalidAmount,
	SourceNotFound,
	TargetNotFound,
	SameSourceAndTarget,
	SourceNotStackable,
	IncompatibleStacks,
	InsufficientSourceQuantity,
	TargetFull,
	InsufficientTargetCapacity,
	InvalidPreparedInstance,
	PreparedInstanceAlreadyPresent,
	DestinationInvalid,
	UnexpectedMutationFailure,
	RollbackFailed,
	InvariantViolation,
}
