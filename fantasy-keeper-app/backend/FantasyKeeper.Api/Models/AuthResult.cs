namespace FantasyKeeper.Api.Models;

public enum AuthRole { Owner, Admin }

public record AuthResult(AuthRole Role, string? TeamId, string? SeasonId);
