namespace Kodoku.World.Containers;

/// <summary>
/// Résultat explicite d'une tentative de transfert whole-item — jamais un simple booléen, même
/// patron que <see cref="WorldContainerOperationResult"/>/<see cref="Kodoku.Player.Inventory.DropResult"/>.
/// Volontairement minimal : ne porte ni <see cref="Kodoku.Items.ItemDefinition"/>, ni
/// <c>Quantity</c>, ni position, ni aucune référence de joueur ou de <see cref="Sandbox.Connection"/> — aucune
/// donnée canonique. C'est un accusé de traitement pour l'appelant (voir
/// <see cref="WorldContainerComponent.ReceiveTransferResult"/>), pas un second canal d'état
/// d'inventaire : le contenu réel voyage uniquement par les snapshots existants
/// (<see cref="WorldContainerSnapshotEntry"/>/<see cref="Kodoku.Player.Inventory.InventorySnapshotEntry"/>).
/// </summary>
public readonly record struct WorldContainerTransferResult
{
	public bool Success { get; }

	/// <summary>Identifiant de corrélation de la requête cliente à l'origine de ce résultat (voir <see cref="WorldContainerComponent.RequestTakeItem"/>/<see cref="WorldContainerComponent.RequestStoreItem"/>) — jamais généré côté host, seulement reflété tel que reçu.</summary>
	public string RequestId { get; }

	public WorldContainerTransferDirection Direction { get; }

	/// <summary>Représentation chaîne canonique (<c>Guid.ToString()</c>) — même convention que <see cref="WorldContainerSnapshotEntry.InstanceId"/>.</summary>
	public string InstanceId { get; }

	public WorldContainerTransferFailureReason FailureReason { get; }

	WorldContainerTransferResult( bool success, string requestId, WorldContainerTransferDirection direction, string instanceId, WorldContainerTransferFailureReason failureReason )
	{
		Success = success;
		RequestId = requestId;
		Direction = direction;
		InstanceId = instanceId;
		FailureReason = failureReason;
	}

	public static WorldContainerTransferResult Ok( string requestId, WorldContainerTransferDirection direction, string instanceId )
		=> new( true, requestId, direction, instanceId, WorldContainerTransferFailureReason.None );

	public static WorldContainerTransferResult Fail( string requestId, WorldContainerTransferDirection direction, string instanceId, WorldContainerTransferFailureReason failureReason )
		=> new( false, requestId, direction, instanceId, failureReason );
}
