using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;

namespace SourceGit.ViewModels
{
    public class RenameTerminal : Popup
    {
        public TerminalInstance Target { get; }

        [Required(ErrorMessage = "Name is required!!!")]
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value, true);
        }

        public RenameTerminal(TerminalInstance target)
        {
            Target = target;
            _name = target.Title;
        }

        public override async Task<bool> Sure()
        {
            Target.Title = _name;
            return true;
        }

        private string _name;
    }
}
