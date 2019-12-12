using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Orts.Common;

namespace Orts.Scripting.Api
{
    public interface INotchController
    {
        float Value { get; set; }
        bool Smooth { get; set; }
        ControllerState NotchStateType { get; set; }
    }
}
