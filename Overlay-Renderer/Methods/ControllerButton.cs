using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Overlay_Renderer.Methods
{
    // Overlay_Renderer.Methods namespace (Overlay-Renderer project)
    public readonly struct ControllerButton
    {
        public readonly Guid DeviceInstanceGuid; // DirectInput device ID
        public readonly int ButtonIndex;         // 0-based button index
        public readonly string DeviceName;       // nice to show in UI

        public ControllerButton(Guid guid, int index, string name)
        {
            DeviceInstanceGuid = guid;
            ButtonIndex = index;
            DeviceName = name;
        }

        public override string ToString()
            => $"{DeviceName} [Button {ButtonIndex + 1}]";
    }

}
