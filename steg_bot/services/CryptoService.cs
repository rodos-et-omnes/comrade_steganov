using System.Security.Cryptography;
using System.Text;

namespace StegBot.services;

public class CryptoService
{
    public (byte[] cipherText, byte[] iv, byte[] salt) Encrypt(string plainText, string password)
    {
        using var aes = Aes.Create();

        var salt = new byte[16];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(salt);
        }

        using var pbkdf2 = new Rfc2898DeriveBytes(password: password, salt: salt, iterations: 100_00, hashAlgorithm: HashAlgorithmName.SHA256);

        aes.Key = pbkdf2.GetBytes(32);
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        byte[] encrypted = encryptor.TransformFinalBlock(Encoding.UTF8.GetBytes(plainText), 0, plainText.Length);
        return (encrypted, aes.IV, salt);
    }
    
    public string Decrypt(byte[] cipherText, byte[] iv, byte[] salt, string password)
    {
        using var aes = Aes.Create();
        using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 100_00, HashAlgorithmName.SHA256);
        aes.Key = pbkdf2.GetBytes(32);
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor();
        byte[] decrypted = decryptor.TransformFinalBlock(cipherText, 0, cipherText.Length);
        return Encoding.UTF8.GetString(decrypted);
    }

    public string GeneratePassword()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
    }
}