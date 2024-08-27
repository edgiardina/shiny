using System.Threading.Tasks;
using Shiny.BluetoothLE;

namespace Shiny;


public interface IAccessorySetupDelegate
{
    Task OnSetup(IPeripheral peripheral);
}