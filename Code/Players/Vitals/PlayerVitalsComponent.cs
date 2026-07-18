using System;

namespace Kodoku.Player.Vitals;

/// <summary>
/// État vital réseau d'un pawn Kodoku (santé, endurance, faim, soif, radiation).
/// Le host reste l'autorité pour toute modification (ADR-0002) : les valeurs sont
/// <c>[Sync(SyncFlags.FromHost)]</c>, et les méthodes de mutation ci-dessous sont des
/// appliqueurs autoritaires **sans attribut RPC** — elles ne sont pas un point d'entrée
/// réseau. Elles doivent être appelées uniquement depuis du code qui s'exécute déjà avec
/// l'autorité host (une future RPC <c>[Rpc.Host(NetFlags.OwnerOnly)]</c> côté appelant, ou un
/// futur système host natif comme la dégradation automatique de faim/soif). Aucun client ne
/// doit pouvoir appeler ces méthodes librement avec des valeurs arbitraires. Aucun point
/// d'entrée réseau n'existe actuellement sur ce composant — le précédent outil de test
/// (<c>PlayerVitalsDebugComponent</c>) a été retiré après validation de la réplication.
/// Ne porte aucune logique de dégradation automatique, de mort ou de présentation — voir docs/architecture/PLAYER_ARCHITECTURE.md.
/// </summary>
public sealed class PlayerVitalsComponent : Component
{
	[Property] public float MaxHealth { get; set; } = 100f;
	[Property] public float MaxStamina { get; set; } = 100f;
	[Property] public float MaxHunger { get; set; } = 100f;
	[Property] public float MaxThirst { get; set; } = 100f;
	[Property] public float MaxRadiation { get; set; } = 100f;

	public event Action VitalsChanged;

	float _health = 100f;
	[Sync( SyncFlags.FromHost )]
	public float Health
	{
		get => _health;
		set => SetVital( ref _health, value, MaxHealth, nameof( Health ) );
	}

	float _stamina = 100f;
	[Sync( SyncFlags.FromHost )]
	public float Stamina
	{
		get => _stamina;
		set => SetVital( ref _stamina, value, MaxStamina, nameof( Stamina ) );
	}

	float _hunger = 100f;
	[Sync( SyncFlags.FromHost )]
	public float Hunger
	{
		get => _hunger;
		set => SetVital( ref _hunger, value, MaxHunger, nameof( Hunger ) );
	}

	float _thirst = 100f;
	[Sync( SyncFlags.FromHost )]
	public float Thirst
	{
		get => _thirst;
		set => SetVital( ref _thirst, value, MaxThirst, nameof( Thirst ) );
	}

	float _radiation;
	[Sync( SyncFlags.FromHost )]
	public float Radiation
	{
		get => _radiation;
		set => SetVital( ref _radiation, value, MaxRadiation, nameof( Radiation ) );
	}

	void SetVital( ref float field, float value, float max, string vitalName )
	{
		var clamped = ClampVital( value, max );
		if ( clamped == field )
			return;

		var previous = field;
		field = clamped;
		Log.Info( $"[PlayerVitalsComponent] {GameObject.Name} ({( IsProxy ? "proxy" : "local" )}) {vitalName}: {previous} -> {clamped}" );
		VitalsChanged?.Invoke();
	}

	static float ClampVital( float value, float max )
	{
		if ( !float.IsFinite( value ) )
			return 0f;
		if ( value < 0f )
			return 0f;
		if ( value > max )
			return max;
		return value;
	}

	// Autoritaire — pas de RPC ici, voir le commentaire de classe. À appeler uniquement
	// depuis un contexte déjà host-authoritative.
	public void TakeDamage( float amount )
	{
		if ( amount <= 0f || !float.IsFinite( amount ) )
			return;

		Health -= amount;
	}

	public void Heal( float amount )
	{
		if ( amount <= 0f || !float.IsFinite( amount ) )
			return;

		Health += amount;
	}

	public void ConsumeStamina( float amount )
	{
		if ( amount <= 0f || !float.IsFinite( amount ) )
			return;

		Stamina -= amount;
	}

	public void RestoreStamina( float amount )
	{
		if ( amount <= 0f || !float.IsFinite( amount ) )
			return;

		Stamina += amount;
	}

	public void SetHunger( float value ) => Hunger = value;

	public void SetThirst( float value ) => Thirst = value;

	/// <summary>
	/// Vrai si une restauration de soif aurait un effet observable — <see cref="Thirst"/> suit la même
	/// convention que <see cref="Health"/>/<see cref="Stamina"/>/<see cref="Hunger"/> (valeur haute =
	/// bon état, plein par défaut ; voir <see cref="ResetVitals"/>), donc une valeur déjà à
	/// <see cref="MaxThirst"/> ne peut plus être restaurée. Utilisée par
	/// <see cref="Kodoku.Player.Inventory.PlayerItemUseComponent"/> pour refuser proprement une
	/// utilisation qui n'aurait aucun effet, avant toute mutation d'inventaire.
	/// </summary>
	public bool CanRestoreThirst => Thirst < MaxThirst;

	/// <summary>
	/// Applicateur autoritaire, même patron que <see cref="Heal"/>/<see cref="RestoreStamina"/> — pas
	/// de RPC ici (voir commentaire de classe), à appeler uniquement depuis un contexte déjà
	/// host-authoritative (ex. <see cref="Kodoku.Player.Inventory.PlayerItemUseComponent.TryUseAuthoritative"/>).
	/// Le clamp à <see cref="MaxThirst"/> est déjà géré par <see cref="SetVital"/> via le setter de
	/// <see cref="Thirst"/> — jamais de dépassement possible.
	/// </summary>
	public void RestoreThirst( float amount )
	{
		if ( amount <= 0f || !float.IsFinite( amount ) )
			return;

		Thirst += amount;
	}

	public void SetRadiation( float value ) => Radiation = value;

	/// <summary>
	/// Remet toutes les vitals à leur valeur par défaut (max, radiation à 0). Conservée après
	/// le retrait du composant de debug qui l'utilisait : primitive autoritaire utile à un futur
	/// système de respawn (non implémenté ici — voir docs/architecture/PLAYER_ARCHITECTURE.md,
	/// « Mort et respawn »). Pas de RPC ici, même règle que le reste de la classe.
	/// </summary>
	public void ResetVitals()
	{
		Health = MaxHealth;
		Stamina = MaxStamina;
		Hunger = MaxHunger;
		Thirst = MaxThirst;
		Radiation = 0f;
	}
}
