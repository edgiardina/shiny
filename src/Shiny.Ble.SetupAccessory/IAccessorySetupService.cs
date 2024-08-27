using System.Threading.Tasks;

namespace Shiny;


public interface IAccessorySetupService
{
    Task Listen();
    Task Stop();
}