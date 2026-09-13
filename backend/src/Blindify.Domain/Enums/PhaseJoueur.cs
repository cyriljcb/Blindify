namespace Blindify.Domain.Enums;

/// <summary>Phase de jeu actuellement active pour un joueur qui (re)rejoint une partie — voir
/// GameHub.ConstruireEtatCourantJoueur, utilisé pour resynchroniser un client après une reconnexion
/// en pleine partie (retour utilisateur : un joueur déconnecté en pleine manche devait sinon
/// attendre la manche suivante avant de pouvoir de nouveau participer).</summary>
public enum PhaseJoueur { Aucune, RoundClassique, BonusMise, BonusQuestion }
