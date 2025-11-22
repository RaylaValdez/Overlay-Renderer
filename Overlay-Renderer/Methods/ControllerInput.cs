using SharpDX.DirectInput;

namespace Overlay_Renderer.Methods
{
    public static class ControllerInput
    {
        private static DirectInput? _directInput;
        private static readonly List<Joystick> _devices = [];
        private static readonly Dictionary<Guid, bool[]> _prevButtons = [];
        private static readonly Queue<ControllerButton> _pressQueue = new();

        private static bool _initialized;

        public static void Initialize()
        {
            if (_initialized)
                return;

            _directInput = new DirectInput();

            foreach (var deviceInstance in _directInput.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly))
            {
                try
                {
                    var joystick = new Joystick(_directInput, deviceInstance.InstanceGuid);

                    joystick.Properties.BufferSize = 128;
                    joystick.Acquire();

                    _devices.Add(joystick);

                    var state = joystick.GetCurrentState();
                    var buttons = state.Buttons ?? [];
                    _prevButtons[deviceInstance.InstanceGuid] = (bool[])buttons.Clone();
                }
                catch (Exception)
                {

                }
            }

            _initialized = true;
        }

        ///<summary>
        ///Call once per frame in main loop
        ///Polls Devices and records button pressed this frame into an internal queue
        ///</summary>
        public static void Update()
        {
            if (!_initialized)
                Initialize();

            foreach (var joystick in _devices)
            {
                try
                {
                    joystick.Poll();
                    var state = joystick.GetCurrentState();
                    var buttons = state.Buttons ?? [];

                    if (!_prevButtons.TryGetValue(joystick.Information.InstanceGuid, out var prev))
                    {
                        prev = new bool[buttons.Length];
                        _prevButtons[joystick.Information.InstanceGuid] = prev;
                    }

                    int len = Math.Min(prev.Length, buttons.Length);
                    for (int i = 0; i < len; i++)
                    {
                        if (!prev[i] && buttons[i])
                        {
                            var info = joystick.Information;
                            _pressQueue.Enqueue(new ControllerButton(info.InstanceGuid, i, info.ProductName));
                        }

                        prev[i] = buttons[i];
                    }
                }
                catch
                {

                }
            }
        }

        /// <summary>
        /// Returns the next button that was pressed since the last call to Update.
        /// Returns true if there was one, false if queue is empty.
        /// </summary>
        public static bool TryGetNextButtonPress(out ControllerButton button)
        {
            if (_pressQueue.Count > 0)
            {
                button = _pressQueue.Dequeue();
                return true;
            }

            button = default;
            return false;
        }


        /// <summary>
        /// (For later) Check whether a specific binding is currently down.
        /// </summary>
        public static bool IsButtonDown(ControllerButton binding)
        {
            foreach (var joystick in _devices)
            {
                if (joystick.Information.InstanceGuid != binding.DeviceInstanceGuid)
                    continue;

                try
                {
                    joystick.Poll();
                    var state = joystick.GetCurrentState();
                    var buttons = state.Buttons ?? [];

                    if (binding.ButtonIndex < 0 || binding.ButtonIndex >= buttons.Length)
                        return false;

                    return buttons[binding.ButtonIndex];
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }
    }
}
