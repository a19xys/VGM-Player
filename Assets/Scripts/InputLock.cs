public static class InputLock {
    public static bool IsLocked { get; private set; }
    public static void Lock() => IsLocked = true;
    public static void Unlock() => IsLocked = false;
}