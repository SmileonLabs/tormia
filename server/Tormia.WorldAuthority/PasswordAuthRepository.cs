using System.Security.Cryptography;
using Npgsql;

internal sealed class PasswordAuthRepository
{
    private readonly NpgsqlDataSource dataSource;
    public PasswordAuthRepository(NpgsqlDataSource dataSource) => this.dataSource = dataSource;

    public async Task<AuthSessionResult> Register(string email, string displayName, string password, CancellationToken ct)
    {
        if (!Valid(email, displayName, password, out var error)) return new(null, null, error);
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 210000, HashAlgorithmName.SHA512, 32);
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        var id = Guid.NewGuid();
        try
        {
            await using (var cmd = new NpgsqlCommand("INSERT INTO app_users (user_id, external_subject, display_name) VALUES (@id, @subject, @name);", connection, tx))
            { cmd.Parameters.AddWithValue("id", id); cmd.Parameters.AddWithValue("subject", "password:" + email.Trim().ToLowerInvariant()); cmd.Parameters.AddWithValue("name", displayName.Trim()); await cmd.ExecuteNonQueryAsync(ct); }
            await using (var cmd = new NpgsqlCommand("INSERT INTO account_credentials (user_id, email, password_hash, password_salt) VALUES (@id,@email,@hash,@salt);", connection, tx))
            { cmd.Parameters.AddWithValue("id", id); cmd.Parameters.AddWithValue("email", email.Trim().ToLowerInvariant()); cmd.Parameters.AddWithValue("hash", Convert.ToBase64String(hash)); cmd.Parameters.AddWithValue("salt", Convert.ToBase64String(salt)); await cmd.ExecuteNonQueryAsync(ct); }
            var token = await CreateSession(connection, tx, id, ct); await tx.CommitAsync(ct); return new(id, token, null);
        }
        catch (PostgresException e) when (e.SqlState == "23505") { await tx.RollbackAsync(ct); return new(null, null, "email_already_registered"); }
    }

    public async Task<AuthSessionResult> Login(string email, string password, CancellationToken ct)
    {
        await using var c = await dataSource.OpenConnectionAsync(ct);
        await using var q = new NpgsqlCommand("SELECT user_id,password_hash,password_salt FROM account_credentials WHERE email=@email;", c);
        q.Parameters.AddWithValue("email", email.Trim().ToLowerInvariant()); await using var r = await q.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return new(null, null, "invalid_credentials");
        var id=r.GetGuid(0); var expected=Convert.FromBase64String(r.GetString(1)); var salt=Convert.FromBase64String(r.GetString(2)); await r.DisposeAsync();
        var actual=Rfc2898DeriveBytes.Pbkdf2(password,salt,210000,HashAlgorithmName.SHA512,32);
        if (!CryptographicOperations.FixedTimeEquals(expected,actual)) return new(null,null,"invalid_credentials");
        await using var tx=await c.BeginTransactionAsync(ct); var token=await CreateSession(c,tx,id,ct); await tx.CommitAsync(ct); return new(id,token,null);
    }

    public async Task<Guid?> Authenticate(string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token)) return null; var h=Hash(token);
        await using var c=await dataSource.OpenConnectionAsync(ct); await using var q=new NpgsqlCommand("SELECT user_id FROM account_sessions WHERE token_hash=@hash AND revoked_at IS NULL AND expires_at>now();",c); q.Parameters.AddWithValue("hash",h); var value=await q.ExecuteScalarAsync(ct); return value is Guid id?id:null;
    }

    public async Task Revoke(string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token)) return;
        await using var c = await dataSource.OpenConnectionAsync(ct);
        await using var q = new NpgsqlCommand("UPDATE account_sessions SET revoked_at=now() WHERE token_hash=@hash AND revoked_at IS NULL;", c);
        q.Parameters.AddWithValue("hash", Hash(token));
        await q.ExecuteNonQueryAsync(ct);
    }
    private static async Task<string> CreateSession(NpgsqlConnection c,NpgsqlTransaction tx,Guid id,CancellationToken ct)
    { var token=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)); await using var q=new NpgsqlCommand("INSERT INTO account_sessions(user_id,token_hash,expires_at) VALUES(@id,@hash,now()+interval '7 days');",c,tx);q.Parameters.AddWithValue("id",id);q.Parameters.AddWithValue("hash",Hash(token));await q.ExecuteNonQueryAsync(ct);return token; }
    private static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
    private static bool Valid(string e,string n,string p,out string error){error="";if(string.IsNullOrWhiteSpace(e)||!e.Contains('@'))error="invalid_email";else if(string.IsNullOrWhiteSpace(n)||n.Trim().Length>64)error="invalid_display_name";else if(string.IsNullOrWhiteSpace(p)||p.Length<10)error="password_too_short";return error.Length==0;}
}
internal sealed record AuthSessionResult(Guid? UserId,string? AccessToken,string? RejectionCode);
