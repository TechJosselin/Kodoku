using System;
using Kodoku.Items;
using Kodoku.Items.Inventory;
using Kodoku.Player;
using Kodoku.Player.Inventory;

namespace Kodoku.Items.Interaction;

/// <summary>
/// Rend un <see cref="WorldItemComponent"/> ramassable via le système de pression stock du moteur
/// (<c>Component.IPressable</c>), déjà branché sur <c>Sandbox.PlayerController</c>
/// (<c>EnablePressing</c>/<c>UseButton</c>) présent sur <c>kodoku_player.prefab</c> — aucun scanner
/// de raycast Kodoku n'est écrit ici : le moteur fournit déjà cette détection (une seule
/// implémentation, jamais appelée pour un proxy — confirmé par lecture du code source du moteur,
/// <c>PlayerController.DefaultControls.cs</c>). Voir docs/architecture/ITEM_ARCHITECTURE.md.
///
/// <see cref="Press"/> ne mute jamais l'état de gameplay directement : il se contente de déclencher
/// <see cref="RequestPickup"/>. Le transport réseau (<see cref="RequestPickup"/>, <c>[Rpc.Host]</c>,
/// résout <see cref="Rpc.Caller"/>) est délibérément séparé de la transaction métier
/// (<see cref="TryPickupAuthoritative"/>, non-RPC, prend une <see cref="Connection"/> explicite) :
/// cette séparation permet à un futur outil de test déterministe d'appeler directement
/// <see cref="TryPickupAuthoritative"/> avec deux connexions distinctes sur la même cible, sans
/// dépendre du transport RPC ni dupliquer la moindre logique de validation.
/// </summary>
public sealed class WorldItemPickupComponent : Component, Component.IPressable
{
	/// <summary>
	/// Borne haute optionnelle propre à cet item (ex. un objet qu'on veut ramassable à plus courte
	/// portée que la portée d'interaction générique). La portée réellement appliquée côté host est
	/// <c>MathF.Min(pawn.PlayerController.ReachLength, MaxPickupDistance)</c> — voir
	/// <see cref="TryPickupAuthoritative"/> — jamais <see cref="MaxPickupDistance"/> seule : une RPC
	/// directe ne doit jamais bénéficier d'une portée supérieure à l'interaction stock qui l'a
	/// déclenchée. `ReachLength` vaut 130 par défaut sur `kodoku_player.prefab` ; 150 ici est
	/// volontairement plus généreux que cette valeur pour que ce soit systématiquement
	/// `ReachLength` (lu dynamiquement par pawn, pas une constante dupliquée) qui fasse foi.
	/// </summary>
	[Group( "Configuration" )]
	[Property]
	public float MaxPickupDistance { get; set; } = 150f;

	/// <summary>
	/// <c>[RequireComponent]</c> résout (ou crée) le <see cref="WorldItemComponent"/> du même
	/// GameObject — usage confirmé par lecture directe de la définition de l'attribut dans le
	/// moteur (<c>[AttributeUsage(AttributeTargets.Property)]</c>, sans argument de constructeur ;
	/// n'existe pas sous forme d'attribut de classe prenant un <c>Type</c>).
	/// </summary>
	[RequireComponent]
	public WorldItemComponent WorldItem { get; set; }

	/// <summary>
	/// Réservation host-only, non répliquée : vraie uniquement sur l'instance du host, jamais
	/// mise à jour sur les clients (aucun <c>[Sync]</c>). N'est donc pas une preuve fiable côté
	/// client que l'objet est pris — seul <see cref="RequestPickup"/> côté host en décide. Existe
	/// pour empêcher qu'une seconde transaction hôte traite le même objet (même patron que
	/// <see cref="LootSpawnPointComponent.HasEvaluated"/>).
	/// </summary>
	[Group( "Runtime" )]
	[ReadOnly]
	[Property]
	public bool IsClaimed { get; private set; }

	// Component.IPressable — implémentation publique implicite, même convention que BaseChair
	// (stock, engine/.../Components/Game/BaseChair.cs). Press() est le seul membre sans corps par
	// défaut dans l'interface ; CanPress()/GetTooltip() sont surchargés ici pour un retour local
	// utile, le reste (Hover/Look/Blur/Pressing/Release) garde son comportement par défaut : un
	// pickup est une action instantanée, pas un maintien.

	public bool CanPress( IPressable.Event e )
	{
		// Vérification locale, indicative uniquement (jamais mise à jour sur un client distant —
		// voir commentaire de IsClaimed). La seule vérification qui compte réellement se fait dans
		// RequestPickup(), côté host.
		return !IsClaimed && WorldItem.IsValid() && WorldItem.IsInitialized;
	}

	public bool Press( IPressable.Event e )
	{
		RequestPickup();

		// false : pas de maintien attendu pour un pickup instantané (voir Component.IPressable.Press,
		// « If it returns true then you should call Release when the press finishes »).
		return false;
	}

	public IPressable.Tooltip? GetTooltip( IPressable.Event e )
	{
		if ( IsClaimed || !WorldItem.IsValid() || WorldItem.Definition is null )
			return null;

		// Le tooltip ne doit jamais apparaître à une distance où le pickup échouerait de toute façon
		// (TryPickupAuthoritative rejette au-delà de cette même portée effective, voir plus bas) : la
		// détection stock du regard (PlayerController.Hovered/Tooltips) n'est, elle, pas bornée par
		// ReachLength — seul le press l'est. Vérification locale/indicative uniquement, comme CanPress —
		// c'est TryPickupAuthoritative qui revalide réellement côté host.
		if ( !IsWithinTooltipRange() )
			return null;

		return new IPressable.Tooltip( $"Ramasser {WorldItem.Definition.DisplayName}", "", "" );
	}

	bool IsWithinTooltipRange()
	{
		var controller = KodokuPlayerComponent.Local?.PlayerController;
		if ( controller is null )
			return false;

		var effectiveRange = MathF.Min( controller.ReachLength, MaxPickupDistance );
		return Vector3.DistanceBetween( controller.EyePosition, WorldItem.GameObject.WorldPosition ) <= effectiveRange;
	}

	/// <summary>
	/// Point d'entrée réseau unique du pickup. <c>[Rpc.Host]</c> route l'exécution sur le host quel
	/// que soit l'appelant — <c>[Rpc.Owner]</c> aurait été incorrect ici : cet objet-monde est
	/// possédé par le host lui-même (spawné uniquement par le host, voir LootSpawnPointComponent),
	/// donc un <c>NetFlags.OwnerOnly</c> aurait empêché tout joueur non-host de jamais l'appeler.
	/// Ne fait confiance à aucune donnée du client au-delà de l'identité de l'appelant
	/// (<see cref="Rpc.Caller"/>) et de l'identité de l'objet ciblé (implicite à cette RPC —
	/// aucun identifiant transmis, l'appel cible directement cette instance réseau précise).
	/// </summary>
	[Rpc.Host]
	public void RequestPickup()
	{
		var caller = Rpc.Caller;
		var result = TryPickupAuthoritative( caller );

		if ( result.Success )
		{
			Log.Info( $"[Pickup][Success] '{caller?.DisplayName}' a ramassé '{WorldItem.Definition.ItemId}' " +
				$"(Instance={WorldItem.Instance.InstanceId})." );
		}
		else
		{
			Log.Warning( $"[Pickup][Fail] '{caller?.DisplayName}' -> '{GameObject.Name}': {result.FailureReason}" );
		}
	}

	/// <summary>
	/// Transaction métier complète du pickup, indépendante de tout transport réseau — ne fait
	/// aucune supposition sur la façon dont <paramref name="requester"/> a été obtenu. Appelée par
	/// <see cref="RequestPickup"/> (RPC réelle, <paramref name="requester"/> = <see cref="Rpc.Caller"/>)
	/// et, plus tard, par un outil de test déterministe qui appellera cette même méthode deux fois
	/// avec deux connexions distinctes sur la même cible — jamais de logique de pickup dupliquée
	/// entre les deux appelants. Public : doit rester appelable directement, sans RPC, par ce futur
	/// outil de test (situé dans un autre composant).
	/// </summary>
	public PickupResult TryPickupAuthoritative( Connection requester )
	{
		// Garde défensive — ne devrait jamais se produire pour un appel host réel (RPC ou test
		// déterministe, tous deux exécutés côté host), mais on ne mute jamais l'état de gameplay
		// sans avoir vérifié l'autorité, même ici.
		if ( !Networking.IsHost )
		{
			Log.Error( $"[Pickup][InternalError] TryPickupAuthoritative() exécuté hors du host pour '{GameObject.Name}'." );
			return PickupResult.Fail( PickupFailureReason.InternalError );
		}

		// 1. Empêcher une seconde transaction de traiter le même objet.
		if ( IsClaimed )
			return PickupResult.Fail( PickupFailureReason.AlreadyClaimed );

		// 2. L'objet existe-t-il encore et est-il valide ?
		if ( !WorldItem.IsValid() || !WorldItem.IsInitialized )
			return PickupResult.Fail( PickupFailureReason.ObjectNotFound );

		// 3. L'appelant correspond-il à un pawn valide ?
		var pawn = KodokuPlayerComponent.FindByConnection( Scene, requester );
		if ( pawn is null || pawn.PlayerController is null )
			return PickupResult.Fail( PickupFailureReason.InvalidCaller );

		var inventory = pawn.Components.Get<PlayerInventoryComponent>();
		if ( inventory is null || inventory.Container is null )
			return PickupResult.Fail( PickupFailureReason.InvalidCaller );

		// 4. Distance joueur-objet, recalculée par le host — jamais celle envoyée par le client
		// (aucune donnée de position n'est d'ailleurs transmise par cette RPC). Portée EFFECTIVE =
		// min(ReachLength du pawn, MaxPickupDistance) : une RPC appelée directement (en contournant
		// l'interaction stock) ne doit jamais bénéficier d'une portée supérieure à ce que
		// l'interaction stock elle-même autoriserait. ReachLength est lu dynamiquement sur le pawn
		// appelant (pas une constante dupliquée), pour rester correct si un pawn futur a une valeur
		// différente de 130.
		var eyePosition = pawn.PlayerController.EyePosition;
		var itemPosition = WorldItem.GameObject.WorldPosition;
		var effectiveRange = MathF.Min( pawn.PlayerController.ReachLength, MaxPickupDistance );

		if ( Vector3.DistanceBetween( eyePosition, itemPosition ) > effectiveRange )
			return PickupResult.Fail( PickupFailureReason.OutOfRange );

		// 5. Ligne de vue ET direction réelle du regard, recalculées par le host, sur cette même
		// portée effective. Le trace part de EyePosition, suit EyeAngles.Forward (la direction où le
		// joueur regarde réellement côté host, confirmée publique par lecture du code source du
		// moteur) — PAS une ligne synthétique tracée directement vers la position connue de l'item.
		// Un joueur qui ne regarde pas l'item échoue donc cette validation même s'il est à portée et
		// sans obstacle « à vol d'oiseau ». `IgnoreGameObjectHierarchy(pawn.GameObject)` exclut toute
		// la hiérarchie du pawn (corps, colliders) du résultat. `Radius(4)` reproduit la tolérance
		// déjà utilisée par la détection locale stock (`PlayerController.Pressing.cs`,
		// `TryGetLookedAt`, sphères de 0 à 4 unités) pour éviter qu'une revalidation host plus
		// stricte que la détection cliente ne rejette à tort un pickup que le joueur voyait pourtant
		// comme disponible.
		var trace = Scene.Trace
			.Ray( eyePosition, eyePosition + pawn.PlayerController.EyeAngles.Forward * effectiveRange )
			.IgnoreGameObjectHierarchy( pawn.GameObject )
			.Radius( 4f )
			.Run();

		// Reconnaît la bouteille même si son collider est porté par un enfant du GameObject qui
		// porte WorldItemPickupComponent : on remonte depuis l'objet touché (GetComponentInParent,
		// includeSelf: true par défaut — confirmé par lecture du code source du moteur,
		// Component.GetComponent.cs) plutôt que de comparer une égalité fragile entre
		// `trace.GameObject` et `GameObject`. `hit.Collider?.GameObject ?? hit.GameObject` reproduit
		// le même repli défensif que `PlayerController.Pressing.cs` (le champ GameObject du résultat
		// peut différer de celui du collider selon la forme du trace). Un autre objet touché avant
		// la bouteille (mur, autre item) fait échouer cette résolution — la bouteille n'étant alors
		// jamais atteinte par le rayon.
		var hitObject = trace.Collider?.GameObject ?? trace.GameObject;
		var hitPickup = hitObject?.GetComponentInParent<WorldItemPickupComponent>();

		if ( !trace.Hit || hitPickup != this )
			return PickupResult.Fail( PickupFailureReason.LineOfSightBlocked );

		// 6. Définition et quantité valides.
		var definition = WorldItem.Definition;
		if ( definition is null || string.IsNullOrEmpty( definition.ItemId ) )
			return PickupResult.Fail( PickupFailureReason.InvalidDefinition );

		// 7. Réservation — verrouille l'objet avant toute mutation d'inventaire. Sûr sans verrou
		// explicite : le modèle d'exécution du host est mono-thread (même hypothèse implicite que
		// LootSpawnPointComponent.HasEvaluated), donc aucune seconde exécution de TryPickup() ne
		// peut s'intercaler entre cette ligne et la mutation ci-dessous.
		IsClaimed = true;

		// 8. Tentative d'ajout dans l'inventaire — jamais de destruction avant confirmation.
		var addResult = inventory.Container.TryAddFirstFit( WorldItem.Instance );
		if ( !addResult.Success )
		{
			// Échec : libérer la réservation, conserver l'objet dans le monde, ne rien détruire.
			IsClaimed = false;

			return addResult.FailureReason == InventoryFailureReason.NoAvailableSpace
				? PickupResult.Fail( PickupFailureReason.InventoryFull )
				: PickupResult.Fail( PickupFailureReason.InternalError );
		}

		// 9. Succès confirmé : notifier l'inventaire du propriétaire AVANT de détruire l'objet-monde
		// (voir PlayerInventoryComponent.NotifyMutated) — le snapshot réseau du propriétaire part avant
		// le signal implicite de succès côté client (la destruction répliquée, ci-dessous), plutôt
		// qu'après, pour qu'un client ne voie jamais l'objet disparaître du monde sans que son
		// inventaire local n'ait déjà commencé à se mettre à jour.
		inventory.NotifyMutated();

		// 10. Détruire l'objet-monde. La destruction, réseau, se propage à tous les clients — c'est le
		// signal implicite de succès côté client (pas de second aller-retour RPC de confirmation, pour
		// éviter l'enchaînement RPC déjà documenté comme risqué dans docs/architecture/MULTIPLAYER_ARCHITECTURE.md).
		GameObject.Destroy();

		return PickupResult.Ok();
	}
}
