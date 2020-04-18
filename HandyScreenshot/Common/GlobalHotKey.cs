﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reactive.Disposables;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using HandyScreenshot.Interop;

namespace HandyScreenshot.Common
{
    public sealed class GlobalHotKey : IDisposable
    {
        private struct Item
        {
            public ModifierKeys ModifierKeys { get; }

            public Key Key { get; }

            public Action Callback { get; }

            public Item(ModifierKeys modifierKeys, Key key, Action callback)
            {
                ModifierKeys = modifierKeys;
                Key = key;
                Callback = callback;
            }
        }

        private const int WM_HOTKEY = 0x0312;
        private static readonly IntPtr Hwnd = (IntPtr)(-3);
        private static HwndSource _hwndSource;

        public static GlobalHotKey Create() => new GlobalHotKey();

        private readonly Dictionary<(ModifierKeys modifier, Key key), Item> _items = new Dictionary<(ModifierKeys modifier, Key key), Item>();
        private Dictionary<int, Action> _callbackMap = new Dictionary<int, Action>();
        private bool _disposed;

        private GlobalHotKey() { }

        public GlobalHotKey Register(ModifierKeys modifier, Key key, Action callback)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(GlobalHotKey));

            if (!IsValidKey(key))
                throw new ArgumentException();

            _items[(modifier, key)] = new Item(modifier, key, callback);

            return this;
        }

        public IDisposable Start()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(GlobalHotKey));

            if (_callbackMap != null)
                throw new InvalidOperationException();

            AddHook(HotKeyHookHandler);

            _callbackMap = new Dictionary<int, Action>();
            foreach (var item in _items.Values)
            {
                _callbackMap[item.GetHashCode()] = item.Callback;
                RegisterHotKey(_hwndSource.Handle, item.GetHashCode(), item.ModifierKeys, item.Key);
            }

            return this;
        }

        private IntPtr HotKeyHookHandler(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (_disposed || msg != WM_HOTKEY) return IntPtr.Zero;

            try
            {
                if (_callbackMap.TryGetValue(wParam.ToInt32(), out var callback))
                {
                    callback?.Invoke();
                }
            }
            catch
            {
                // TODO: Logging
            }

            return IntPtr.Zero;
        }

        void IDisposable.Dispose()
        {
            if (_disposed || _hwndSource == null) return;

            _disposed = true;
            _hwndSource.RemoveHook(HotKeyHookHandler);
            foreach (var item in _items)
            {
                UnregisterHotKey(_hwndSource.Handle, item.GetHashCode());
            }
        }

        private static void AddHook(HwndSourceHook messageHook)
        {
            const string windowName = "HandyScreenshot";

            if (_hwndSource == null)
            {
                var parameters = new HwndSourceParameters(windowName)
                {
                    HwndSourceHook = messageHook,
                    ParentWindow = Hwnd
                };
                _hwndSource = new HwndSource(parameters);
            }
            else
            {
                _hwndSource.AddHook(messageHook);
            }
        }

        private static void RegisterHotKey(IntPtr hWnd, int id, ModifierKeys modifiers, Key keys)
        {
            var success = NativeMethods.RegisterHotKey(hWnd, id, modifiers, KeyInterop.VirtualKeyFromKey(keys));

            if (!success)
            {
                var errCode = Marshal.GetLastWin32Error();
                throw new Win32Exception(errCode);
            }
        }

        private static void UnregisterHotKey(IntPtr hWnd, int id)
        {
            if (_hwndSource != null)
            {
                NativeMethods.UnregisterHotKey(hWnd, id);
            }
        }

        private static bool IsValidKey(Key key) => key >= Key.D0 && key <= Key.Z;
    }
}
