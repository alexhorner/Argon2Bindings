﻿using System;
using System.Runtime.InteropServices;
using System.Text;
using static Argon2Bindings.Utilities;

namespace Argon2Bindings;

/* Todo: Add argon2 LICENSE file, binary, provide link to exact version of source which was used to compile binary */
/* Todo: Add argon2 lib comments */
/* Todo: Add test project */
/* Todo: Automate building argon2 platform binaries (using Docker?) */
/*
 **Note**
 Always malloc mem for functions where it's 
 expecting a pointer to memory (unless told otherwise)
 
 Steps (Think in terms of IDisposable):
 1. Malloc
 2. Call
 3. Free
*/

public static class Argon2Core
{
    public static Argon2HashResult HashRaw(
        string password,
        string salt,
        Argon2Context? context = null)
    {
        ValidatePasswordAndSaltStrings(password, salt);

        var passwordBytes = Encoding.UTF8.GetBytes(password);
        var saltBytes = Encoding.UTF8.GetBytes(salt);

        return HashRaw(passwordBytes, saltBytes, context);
    }

    public static Argon2HashResult HashRaw(
        byte[] password,
        byte[] salt,
        Argon2Context? context = null)
    {
        ValidatePasswordAndSaltCollections(password, salt);

        context ??= new();

        return Hash(password, salt, context, false);
    }

    public static Argon2HashResult HashEncoded(
        string password,
        string salt,
        Argon2Context? context = null)
    {
        ValidatePasswordAndSaltStrings(password, salt);

        var passwordBytes = Encoding.UTF8.GetBytes(password);
        var saltBytes = Encoding.UTF8.GetBytes(salt);

        return HashEncoded(passwordBytes, saltBytes, context);
    }

    public static Argon2HashResult HashEncoded(
        byte[] password,
        byte[] salt,
        Argon2Context? context = null)
    {
        ValidatePasswordAndSaltCollections(password, salt);

        context ??= new();

        return Hash(password, salt, context);
    }

    public static Argon2HashResult Hash(
        string password,
        string salt,
        Argon2Context context,
        bool encodeHash = true)
    {
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        var saltBytes = Encoding.UTF8.GetBytes(salt);

        var result = Hash(passwordBytes, saltBytes, context, encodeHash);
        return result;
    }

    private static Argon2HashResult Hash(
        byte[] passwordBytes,
        byte[] saltBytes,
        Argon2Context context,
        bool encodeHash = true)
    {
        bool rawHashRequested = !encodeHash;

        nuint passwordLength = Convert.ToUInt32(passwordBytes.Length);
        nuint saltLength = Convert.ToUInt32(saltBytes.Length);
        nuint encodedLength = rawHashRequested
            ? 0
            : GetEncodedHashLength(
                context.TimeCost,
                context.MemoryCost,
                context.DegreeOfParallelism,
                saltLength,
                context.HashLength,
                context.Type);

        bool errored = false;
        byte[] outputBytes = { };
        Argon2Result result = Argon2Result.Ok;

        IntPtr passPtr = default,
            saltPtr = default,
            rawHashBufferPointer = rawHashRequested
                ? Marshal.AllocHGlobal(Convert.ToInt32(context.HashLength))
                : IntPtr.Zero,
            encodedBufferPointer = rawHashRequested
                ? IntPtr.Zero
                : Marshal.AllocHGlobal(Convert.ToInt32(encodedLength));

        void SafelyFreePointer(IntPtr pointer)
        {
            if (pointer == IntPtr.Zero) return;
            Marshal.FreeHGlobal(pointer);
        }

        void FreeManagedPointers()
        {
            SafelyFreePointer(passPtr);
            SafelyFreePointer(saltPtr);
            SafelyFreePointer(rawHashBufferPointer);
            SafelyFreePointer(encodedBufferPointer);
        }

        try
        {
            passPtr = GetPointerToBytes(passwordBytes);
            saltPtr = GetPointerToBytes(saltBytes);

            result = Argon2Library.argon2_hash(
                context.TimeCost,
                context.MemoryCost,
                context.DegreeOfParallelism,
                passPtr,
                passwordLength,
                saltPtr,
                saltLength,
                rawHashBufferPointer,
                context.HashLength,
                encodedBufferPointer,
                encodedLength,
                context.Type,
                context.Version);

            /* Todo: Throw an exception when no success, or return error w/ empty / incomplete data? */
            /*if (result is not Argon2Result.Ok)
                throw new Exception(Argon2Errors.GetErrorMessage(result));*/

            /* Todo: TrimRight null-terminator byte (\x00) */
            outputBytes = rawHashRequested
                ? GetBytesFromPointer(rawHashBufferPointer, Convert.ToInt32(context.HashLength))
                : GetBytesFromPointer(encodedBufferPointer, Convert.ToInt32(encodedLength));
        }
        catch (Exception e)
        {
            errored = true;
            FreeManagedPointers();
            WriteError($"{e.Message}\n {e.StackTrace}");
        }
        finally
        {
            if (!errored)
                FreeManagedPointers();
        }

        var encodedForm = rawHashRequested
            ? Convert.ToBase64String(outputBytes)
            : Encoding.UTF8.GetString(outputBytes);

        return new(result, outputBytes, encodedForm);
    }

    private static void ValidatePasswordAndSaltStrings(string password, string salt)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Value cannot be null or empty.", nameof(password));

        if (string.IsNullOrEmpty(salt))
            throw new ArgumentException("Value cannot be null or empty.", nameof(salt));
    }

    private static void ValidatePasswordAndSaltCollections(byte[] password, byte[] salt)
    {
        if (password is null || password.Length <= 0)
            throw new ArgumentException("Value cannot be null or an empty collection.", nameof(password));

        if (salt is null || salt.Length <= 0)
            throw new ArgumentException("Value cannot be null or an empty collection.", nameof(salt));
    }

    public static string GetErrorMessage(
        Argon2Result error)
    {
        var messagePtr = Argon2Library.argon2_error_message(error);
        return Marshal.PtrToStringAnsi(messagePtr) ?? string.Empty;
    }

    private static nuint GetEncodedHashLength(
        nuint timeCost,
        nuint memoryCost,
        nuint degreeOfParallelism,
        nuint saltLength,
        nuint hashLength,
        Argon2Type type)
    {
        var length = Argon2Library.argon2_encodedlen(
            timeCost,
            memoryCost,
            degreeOfParallelism,
            saltLength,
            hashLength,
            type
        );

        return length;
    }

    private static byte[] GetBytesFromPointer(
        IntPtr ptr,
        int length)
    {
        byte[] outBytes = new byte[length];
        Marshal.Copy(ptr, outBytes, 0, length);

        return outBytes;
    }

    private static IntPtr GetPointerToBytes(
        byte[] array)
    {
        IntPtr ptr = Marshal.AllocHGlobal(array.Length);
        Marshal.Copy(array, 0, ptr, array.Length);

        return ptr;
    }
}