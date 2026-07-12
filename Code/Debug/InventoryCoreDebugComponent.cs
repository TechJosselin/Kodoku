using System;
using Kodoku.Items;
using Kodoku.Items.Inventory;

namespace Kodoku.Debugging;

/// <summary>
/// OUTIL TEMPORAIRE — valide manuellement <see cref="InventoryContainer"/> (Tests A à K plus
/// L à O pour les vérifications ciblées supplémentaires, docs/architecture/ITEM_ARCHITECTURE.md).
/// Pas un système de gameplay, pas de réseau, pas de système natif d'inventaire. À installer
/// manuellement sous <c>_Debug</c> dans <c>GameplayTest.scene</c>, jamais commité une fois la
/// scène modifiée pour ce test.
///
/// <see cref="ContainerWidth"/>/<see cref="ContainerHeight"/> ne sont qu'une taille de base :
/// chaque test agrandit au besoin pour garantir ses préconditions réelles (assez de place pour
/// deux emplacements, etc.), sauf les Tests D et J qui exigent délibérément un conteneur
/// dimensionné exactement sur l'item testé (hors limites / inventaire plein) et ignorent donc
/// cette taille de base.
/// </summary>
public sealed class InventoryCoreDebugComponent : Component
{
	[Group( "Configuration" )]
	[Property]
	public ItemDefinition TestItemDefinition { get; set; }

	[Group( "Configuration" )]
	[Property]
	public int ContainerWidth { get; set; } = 4;

	[Group( "Configuration" )]
	[Property]
	public int ContainerHeight { get; set; } = 4;

	[Group( "Configuration" )]
	[Property]
	public bool RunTestsOnStart { get; set; } = true;

	private bool _hasRun;

	protected override void OnStart()
	{
		if ( !RunTestsOnStart || _hasRun )
			return;

		_hasRun = true;
		RunInventoryCoreTests();
	}

	public void RunInventoryCoreTests()
	{
		if ( TestItemDefinition is null )
		{
			Log.Error( "[InventoryCoreTest] TestItemDefinition is not assigned — aborting." );
			return;
		}

		if ( string.IsNullOrEmpty( TestItemDefinition.ItemId ) )
		{
			Log.Error( "[InventoryCoreTest] TestItemDefinition.ItemId is empty — aborting." );
			return;
		}

		Report( "A", Test_ValidAdd() );
		Report( "B", Test_Duplicate() );
		Report( "C", Test_Collision() );
		Report( "D", Test_OutOfBounds() );
		Report( "E", Test_Rotation() );
		Report( "F", Test_ValidMove() );
		Report( "G", Test_InvalidMoveAtomic() );
		Report( "H", Test_Remove() );
		Report( "I", Test_FirstFit() );
		Report( "J", Test_FullContainer() );
		Report( "K", Test_InvariantsAfterFailures() );
		Report( "L", Test_SameGuidDifferentReference() );
		Report( "M", Test_RotationDuringInvalidMove() );
		Report( "N", Test_CellReuseAfterRemoval() );
		Report( "O", Test_Weight() );
	}

	static void Report( string testId, (bool success, string reason) outcome )
	{
		if ( outcome.success )
			Log.Info( $"[InventoryCoreTest][{testId}][PASS]" );
		else
			Log.Warning( $"[InventoryCoreTest][{testId}][FAIL] {outcome.reason}" );
	}

	ItemInstance CreateItem() => ItemInstance.CreateNew( TestItemDefinition, 1 );

	int EffectiveWidth( int minimumRequired ) => Math.Max( ContainerWidth, minimumRequired );
	int EffectiveHeight( int minimumRequired ) => Math.Max( ContainerHeight, minimumRequired );

	(bool, string) Test_ValidAdd()
	{
		int iw = TestItemDefinition.GridWidth;
		int ih = TestItemDefinition.GridHeight;
		var container = new InventoryContainer( EffectiveWidth( iw ), EffectiveHeight( ih ) );
		var item = CreateItem();

		var result = container.TryAdd( item, 0, 0 );
		if ( !result.Success )
			return (false, $"TryAdd failed: {result.FailureReason}");

		if ( container.Count != 1 )
			return (false, $"Count expected 1, got {container.Count}");

		var placement = container.GetPlacement( item.InstanceId );
		if ( placement is null || placement.X != 0 || placement.Y != 0 || placement.IsRotated )
			return (false, "Placement does not match expected position/rotation.");

		return (true, null);
	}

	(bool, string) Test_Duplicate()
	{
		int iw = TestItemDefinition.GridWidth;
		int ih = TestItemDefinition.GridHeight;
		var container = new InventoryContainer( EffectiveWidth( iw * 2 ), EffectiveHeight( ih ) );
		var item = CreateItem();

		container.TryAdd( item, 0, 0 );
		var result = container.TryAdd( item, iw, 0 );

		if ( result.Success || result.FailureReason != InventoryFailureReason.AlreadyContained )
			return (false, $"Expected AlreadyContained, got Success={result.Success} Reason={result.FailureReason}");

		if ( container.Count != 1 )
			return (false, $"Count expected 1, got {container.Count}");

		return (true, null);
	}

	(bool, string) Test_Collision()
	{
		int iw = TestItemDefinition.GridWidth;
		int ih = TestItemDefinition.GridHeight;
		var container = new InventoryContainer( EffectiveWidth( iw ), EffectiveHeight( ih ) );
		var item1 = CreateItem();
		var item2 = CreateItem();

		container.TryAdd( item1, 0, 0 );
		var result = container.TryAdd( item2, 0, 0 );

		if ( result.Success || result.FailureReason != InventoryFailureReason.Overlapping )
			return (false, $"Expected Overlapping, got Success={result.Success} Reason={result.FailureReason}");

		if ( container.Count != 1 )
			return (false, $"Count expected 1, got {container.Count}");

		return (true, null);
	}

	(bool, string) Test_OutOfBounds()
	{
		int iw = TestItemDefinition.GridWidth;
		int ih = TestItemDefinition.GridHeight;
		// Conteneur dimensionné exactement sur l'item : toute position autre que (0,0) dépasse.
		var container = new InventoryContainer( iw, ih );

		foreach ( var (x, y) in new[] { (iw, 0), (0, ih), (-1, 0), (0, -1) } )
		{
			var item = CreateItem();
			var result = container.TryAdd( item, x, y );

			if ( result.Success || result.FailureReason != InventoryFailureReason.OutOfBounds )
				return (false, $"Position ({x},{y}): expected OutOfBounds, got Success={result.Success} Reason={result.FailureReason}");
		}

		if ( container.Count != 0 )
			return (false, $"Count expected 0, got {container.Count}");

		return (true, null);
	}

	(bool, string) Test_Rotation()
	{
		int iw = TestItemDefinition.GridWidth;
		int ih = TestItemDefinition.GridHeight;
		int side = Math.Max( iw, ih );
		// Carré dimensionné sur la plus grande dimension : les deux orientations tiennent en (0,0).
		var container = new InventoryContainer( EffectiveWidth( side ), EffectiveHeight( side ) );
		var item = CreateItem();

		var result = container.CanPlace( item, 0, 0, rotated: true );

		if ( !TestItemDefinition.CanRotate )
		{
			if ( result.Success || result.FailureReason != InventoryFailureReason.RotationNotAllowed )
				return (false, $"CanRotate=false: expected RotationNotAllowed, got Success={result.Success} Reason={result.FailureReason}");

			return (true, null);
		}

		if ( !result.Success )
			return (false, $"CanRotate=true: expected success, got {result.FailureReason}");

		if ( result.Placement.Width != ih || result.Placement.Height != iw || !result.Placement.IsRotated )
			return (false, "Rotated placement dimensions do not match swapped GridWidth/GridHeight.");

		return (true, null);
	}

	(bool, string) Test_ValidMove()
	{
		int iw = TestItemDefinition.GridWidth;
		int ih = TestItemDefinition.GridHeight;
		var container = new InventoryContainer( EffectiveWidth( iw * 2 ), EffectiveHeight( ih ) );
		var item = CreateItem();

		container.TryAdd( item, 0, 0 );
		var result = container.TryMove( item.InstanceId, iw, 0, rotated: false );

		if ( !result.Success )
			return (false, $"TryMove failed: {result.FailureReason}");

		if ( container.Count != 1 )
			return (false, $"Count expected 1, got {container.Count}");

		var placement = container.GetPlacement( item.InstanceId );
		if ( placement is null || placement.X != iw || placement.Y != 0 )
			return (false, "Placement does not reflect the new position.");

		// L'ancienne cellule doit être libérée : un item de contrôle doit pouvoir s'y placer.
		var probe = CreateItem();
		if ( !container.CanPlace( probe, 0, 0, rotated: false ).Success )
			return (false, "Old cell was not freed after the move.");

		return (true, null);
	}

	(bool, string) Test_InvalidMoveAtomic()
	{
		int iw = TestItemDefinition.GridWidth;
		int ih = TestItemDefinition.GridHeight;
		var container = new InventoryContainer( EffectiveWidth( iw * 2 ), EffectiveHeight( ih ) );
		var item1 = CreateItem();
		var item2 = CreateItem();

		container.TryAdd( item1, 0, 0 );
		container.TryAdd( item2, iw, 0 );

		var overlapMove = container.TryMove( item1.InstanceId, iw, 0, rotated: false );
		if ( overlapMove.Success || overlapMove.FailureReason != InventoryFailureReason.Overlapping )
			return (false, $"Overlap move: expected Overlapping, got Success={overlapMove.Success} Reason={overlapMove.FailureReason}");

		// Cible garantie hors limites indépendamment de ContainerWidth/ContainerHeight, des dimensions
		// de l'item et de sa rotation : targetX = container.Width fait toujours déborder (targetX +
		// largeur > Width dès lors que la largeur placée est >= 1).
		int invalidX = container.Width;
		var originalPlacement = container.GetPlacement( item1.InstanceId );

		var boundsMove = container.TryMove( item1.InstanceId, invalidX, 0, rotated: false );
		if ( boundsMove.Success || boundsMove.FailureReason != InventoryFailureReason.OutOfBounds )
			return (false, $"Bounds move: expected OutOfBounds, got Success={boundsMove.Success} Reason={boundsMove.FailureReason}");

		if ( container.Count != 2 )
			return (false, $"Count expected 2, got {container.Count}");

		var placement = container.GetPlacement( item1.InstanceId );
		if ( placement is null )
			return (false, "item1 is no longer retrievable by InstanceId after a failed move.");

		if ( placement.X != originalPlacement.X || placement.Y != originalPlacement.Y || placement.IsRotated != originalPlacement.IsRotated )
			return (false, "item1 placement (position, Y or rotation) changed despite failed moves.");

		if ( !container.TryValidateState( out var error ) )
			return (false, $"TryValidateState failed: {error}");

		return (true, null);
	}

	(bool, string) Test_Remove()
	{
		int iw = TestItemDefinition.GridWidth;
		int ih = TestItemDefinition.GridHeight;
		var container = new InventoryContainer( EffectiveWidth( iw ), EffectiveHeight( ih ) );
		var item = CreateItem();

		container.TryAdd( item, 0, 0 );
		var result = container.TryRemove( item.InstanceId, out var removed );

		if ( !result.Success )
			return (false, $"TryRemove failed: {result.FailureReason}");

		if ( !ReferenceEquals( removed, item ) || removed.InstanceId != item.InstanceId )
			return (false, "Removed instance does not match the original reference/InstanceId.");

		if ( container.Count != 0 )
			return (false, $"Count expected 0, got {container.Count}");

		var probe = CreateItem();
		if ( !container.CanPlace( probe, 0, 0, rotated: false ).Success )
			return (false, "Cell was not freed after removal.");

		return (true, null);
	}

	(bool, string) Test_FirstFit()
	{
		int iw = TestItemDefinition.GridWidth;
		int ih = TestItemDefinition.GridHeight;

		(bool, string) RunOnce( out int foundX, out int foundY )
		{
			var container = new InventoryContainer( EffectiveWidth( iw * 3 ), EffectiveHeight( ih ) );
			var filler = CreateItem();
			container.TryAdd( filler, 0, 0 );

			var newItem = CreateItem();
			var result = container.TryAddFirstFit( newItem, allowRotation: false );

			foundX = result.Success ? result.Placement.X : -1;
			foundY = result.Success ? result.Placement.Y : -1;

			if ( !result.Success )
				return (false, $"TryAddFirstFit failed: {result.FailureReason}");

			if ( result.Placement.X != iw || result.Placement.Y != 0 )
				return (false, $"Expected first free slot at ({iw},0), got ({result.Placement.X},{result.Placement.Y}).");

			return (true, null);
		}

		var first = RunOnce( out int x1, out int y1 );
		if ( !first.Item1 )
			return first;

		var second = RunOnce( out int x2, out int y2 );
		if ( !second.Item1 )
			return second;

		if ( x1 != x2 || y1 != y2 )
			return (false, $"Non-deterministic result: ({x1},{y1}) vs ({x2},{y2}).");

		return (true, null);
	}

	(bool, string) Test_FullContainer()
	{
		int iw = TestItemDefinition.GridWidth;
		int ih = TestItemDefinition.GridHeight;
		// Conteneur dimensionné exactement sur l'item : une seule place possible, jamais deux.
		var container = new InventoryContainer( iw, ih );
		var item = CreateItem();
		container.TryAdd( item, 0, 0 );

		var extra = CreateItem();
		var result = container.TryAddFirstFit( extra );

		if ( result.Success || result.FailureReason != InventoryFailureReason.NoAvailableSpace )
			return (false, $"Expected NoAvailableSpace, got Success={result.Success} Reason={result.FailureReason}");

		if ( container.Count != 1 )
			return (false, $"Count expected 1, got {container.Count}");

		return (true, null);
	}

	(bool, string) Test_InvariantsAfterFailures()
	{
		int iw = TestItemDefinition.GridWidth;
		int ih = TestItemDefinition.GridHeight;
		var container = new InventoryContainer( EffectiveWidth( iw * 2 ), EffectiveHeight( ih * 2 ) );
		var item1 = CreateItem();
		var item2 = CreateItem();

		container.TryAdd( item1, 0, 0 );

		// Échecs volontaires, aucun ne doit muter l'état.
		container.TryAdd( item1, iw, 0 );
		container.TryMove( item1.InstanceId, iw * 3, 0, rotated: false );
		container.TryRemove( Guid.NewGuid(), out _ );

		container.TryAdd( item2, iw, 0 );

		if ( !container.TryValidateState( out var error ) )
			return (false, $"TryValidateState failed: {error}");

		if ( container.Count != 2 )
			return (false, $"Count expected 2, got {container.Count}");

		if ( container.GetPlacement( item1.InstanceId ) is null || container.GetPlacement( item2.InstanceId ) is null )
			return (false, "One of the surviving items is not retrievable by InstanceId.");

		return (true, null);
	}

	/// <summary>
	/// Deux références <see cref="ItemInstance"/> distinctes (une créée, une restaurée via
	/// <see cref="ItemInstance.Restore"/>) partageant le même <see cref="ItemInstance.InstanceId"/> —
	/// la seconde doit être refusée, l'identité logique étant l'InstanceId, pas la référence
	/// d'objet.
	/// </summary>
	(bool, string) Test_SameGuidDifferentReference()
	{
		int iw = TestItemDefinition.GridWidth;
		int ih = TestItemDefinition.GridHeight;
		var container = new InventoryContainer( EffectiveWidth( iw * 2 ), EffectiveHeight( ih ) );
		var item1 = CreateItem();
		container.TryAdd( item1, 0, 0 );

		var item2 = ItemInstance.Restore( item1.InstanceId, TestItemDefinition, 1 );
		if ( ReferenceEquals( item1, item2 ) )
			return (false, "ItemInstance.Restore returned the same reference — cannot exercise this scenario.");

		var result = container.TryAdd( item2, iw, 0 );
		if ( result.Success || result.FailureReason != InventoryFailureReason.AlreadyContained )
			return (false, $"Expected AlreadyContained, got Success={result.Success} Reason={result.FailureReason}");

		if ( container.Count != 1 )
			return (false, $"Count expected 1, got {container.Count}");

		return (true, null);
	}

	(bool, string) Test_RotationDuringInvalidMove()
	{
		int iw = TestItemDefinition.GridWidth;
		int ih = TestItemDefinition.GridHeight;
		int containerWidth = EffectiveWidth( iw * 2 );
		var container = new InventoryContainer( containerWidth, EffectiveHeight( ih ) );
		var item = CreateItem();
		container.TryAdd( item, 0, 0, rotated: false );

		// x = containerWidth dépasse toujours les limites, quelle que soit la rotation demandée.
		var result = container.TryMove( item.InstanceId, containerWidth, 0, rotated: true );
		if ( result.Success )
			return (false, "Expected the rotated move to fail, but it succeeded.");

		var placement = container.GetPlacement( item.InstanceId );
		if ( placement is null || placement.X != 0 || placement.Y != 0 || placement.IsRotated )
			return (false, "Original placement (position or rotation) changed despite a failed move.");

		return (true, null);
	}

	(bool, string) Test_CellReuseAfterRemoval()
	{
		int iw = TestItemDefinition.GridWidth;
		int ih = TestItemDefinition.GridHeight;
		var container = new InventoryContainer( EffectiveWidth( iw ), EffectiveHeight( ih ) );
		var itemA = CreateItem();

		container.TryAdd( itemA, 0, 0 );
		container.TryRemove( itemA.InstanceId, out _ );

		var itemB = CreateItem();
		var result = container.TryAdd( itemB, 0, 0 );

		if ( !result.Success )
			return (false, $"Expected success placing itemB at the freed cell, got {result.FailureReason}");

		if ( container.Count != 1 )
			return (false, $"Count expected 1, got {container.Count}");

		return (true, null);
	}

	(bool, string) Test_Weight()
	{
		int iw = TestItemDefinition.GridWidth;
		int ih = TestItemDefinition.GridHeight;
		var container = new InventoryContainer( EffectiveWidth( iw * 2 ), EffectiveHeight( ih ) );

		if ( !NearlyEqual( container.CurrentWeight, 0f ) )
			return (false, $"Expected CurrentWeight=0 on an empty container, got {container.CurrentWeight}");

		var item1 = CreateItem();
		container.TryAdd( item1, 0, 0 );
		float expected = TestItemDefinition.Weight * item1.Quantity;
		if ( !NearlyEqual( container.CurrentWeight, expected ) )
			return (false, $"After add: expected CurrentWeight={expected}, got {container.CurrentWeight}");

		// Échec attendu (collision) — le poids ne doit pas bouger.
		var item2 = CreateItem();
		container.TryAdd( item2, 0, 0 );
		if ( !NearlyEqual( container.CurrentWeight, expected ) )
			return (false, $"After a failed add: CurrentWeight changed unexpectedly to {container.CurrentWeight}");

		// Déplacement — le poids ne doit pas bouger.
		container.TryMove( item1.InstanceId, iw, 0, rotated: false );
		if ( !NearlyEqual( container.CurrentWeight, expected ) )
			return (false, $"After a move: CurrentWeight changed unexpectedly to {container.CurrentWeight}");

		container.TryRemove( item1.InstanceId, out _ );
		if ( !NearlyEqual( container.CurrentWeight, 0f ) )
			return (false, $"After removal: expected CurrentWeight=0, got {container.CurrentWeight}");

		return (true, null);
	}

	static bool NearlyEqual( float a, float b ) => MathF.Abs( a - b ) < 0.0001f;
}
