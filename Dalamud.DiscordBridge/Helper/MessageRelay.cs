using System;
using System.Runtime.InteropServices;
using System.Text;
using Dalamud.Game;
using FFXIVClientStructs.FFXIV.Client.System.Memory;
using FFXIVClientStructs.FFXIV.Client.System.String;
using Framework = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework;


namespace Dalamud.DiscordBridge.Helper;

/// A class containing chat functionality
public class MessageRelay
{
    private const string SendChat = "48 89 5C 24 ?? 48 89 74 24 ?? 57 48 83 EC 20 48 8B F2 48 8B F9 45 84 C9";

    private delegate void ProcessChatBoxDelegate(IntPtr uiModule, IntPtr message, IntPtr unused, byte a4);

    private ProcessChatBoxDelegate? ProcessChatBox { get; }

    internal MessageRelay(ISigScanner scanner)
    {
        if (scanner.TryScanText(SendChat, out var processChatBoxPtr))
        {
            ProcessChatBox = Marshal.GetDelegateForFunctionPointer<ProcessChatBoxDelegate>(processChatBoxPtr);
        }
    }

    /// <summary>
    /// Send a given message to the chat box. <b>This can send chat to the server.</b>
    /// </summary>
    /// <b>This method is unsafe.</b> This method does no checking on your input and
    /// may send content to the server that the normal client could not. You must
    /// verify what you're sending and handle content and length to properly use this.
    /// <param name="message">Message to send</param>
    /// <exception cref="InvalidOperationException">If the signature for this function could not be found</exception>
    public unsafe void SendMessageUnsafe(byte[] message)
    {
        if (ProcessChatBox == null)
        {
            throw new InvalidOperationException("Could not find signature for chat sending (SendMessageUnsafe)");
        }

        var uiModule = (IntPtr)Framework.Instance()->GetUIModule();

        using var payload = new ChatPayload(message);
        var mem1 = Marshal.AllocHGlobal(400);
        Marshal.StructureToPtr(payload, mem1, false);

        ProcessChatBox(uiModule, mem1, IntPtr.Zero, 0);
        Marshal.FreeHGlobal(mem1);
    }

    /// Send a given message to the chat box. <b>This can send chat to the server.</b>
    /// This method is slightly less unsafe than <see cref="SendMessageUnsafe"/>. It
    /// will throw exceptions for certain inputs that the client can't normally send,
    /// but it is still possible to make mistakes. Use with caution.
    /// <param name="message">message to send</param>
    /// <exception cref="ArgumentException">If <paramref name="message"/> is empty, longer than 500 bytes in UTF-8, or contains invalid characters.</exception>
    public void SendMessage(string message)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        switch (bytes.Length)
        {
            case 0:
                throw new ArgumentException("message is empty", nameof(message));
            case > 500:
                throw new ArgumentException("message is longer than 500 bytes", nameof(message));
        }

        if (message.Length != SanitiseText(message).Length)
        {
            throw new ArgumentException("message contained invalid characters", nameof(message));
        }

        SendMessageUnsafe(bytes);
    }

    /// Sanitises a string by removing any invalid input.
    /// The result of this method is safe to use with <see cref="SendMessage"/>, provided that it is not empty or too long.
    /// <param name="text">text to sanitise</param>
    /// <returns>sanitised text</returns>
    public unsafe string SanitiseText(string text)
    {
        var uText = Utf8String.FromString(text);

        uText->SanitizeString(0x27F, (Utf8String*)IntPtr.Zero);
        var sanitised = uText->ToString();

        uText->Dtor();
        IMemorySpace.Free(uText);

        return sanitised;
    }

    [StructLayout(LayoutKind.Explicit)]
    private readonly struct ChatPayload : IDisposable
    {
        [FieldOffset(0)]
        private readonly IntPtr textPtr;

        [FieldOffset(16)]
        private readonly ulong textLen;

        [FieldOffset(8)]
        private readonly ulong unk1;

        [FieldOffset(24)]
        private readonly ulong unk2;

        internal ChatPayload(byte[] stringBytes)
        {
            textPtr = Marshal.AllocHGlobal(stringBytes.Length + 30);
            Marshal.Copy(stringBytes, 0, this.textPtr, stringBytes.Length);
            Marshal.WriteByte(this.textPtr + stringBytes.Length, 0);

            textLen = (ulong)(stringBytes.Length + 1);

            unk1 = 64;
            unk2 = 0;
        }

        public void Dispose()
        {
            Marshal.FreeHGlobal(textPtr);
        }
    }
}