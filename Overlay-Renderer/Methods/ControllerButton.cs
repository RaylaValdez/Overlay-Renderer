namespace Overlay_Renderer.Methods
{
    public readonly struct ControllerButton
    {
        public readonly Guid DeviceInstanceGuid; 
        public readonly int ButtonIndex;         
        public readonly string DeviceName;       

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
