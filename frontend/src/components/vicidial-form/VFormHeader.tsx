import RevolutionLogo from '../../images/RevolutionLogo.png';

interface VicidialFormHeaderProps {
  onClose: () => void;
}

export default function VicidialFormHeader({ onClose }: VicidialFormHeaderProps) {
  return (
    <header className="sticky top-0 z-10 bg-pure-surface dark:bg-gray-900 border-b border-whisper-border dark:border-gray-800">
      <div className="max-w-3xl mx-auto px-4 sm:px-6 h-16 flex justify-between items-center">
        <div className="flex items-center gap-3">
          <img src={RevolutionLogo} alt="RevolutionMedia" className="h-9 w-9" />
          <div>
            <h1 className="text-base font-bold text-primary dark:text-gray-100 leading-tight">ALTRX Sales Form</h1>
            <p className="text-[11px] text-secondary dark:text-gray-400 leading-tight">Registrar venta desde Vicidial</p>
          </div>
        </div>
        <button
          onClick={onClose}
          className="flex items-center gap-1.5 px-3 py-1.5 rounded-md text-sm text-secondary dark:text-gray-300 hover:bg-surface-container-low dark:hover:bg-gray-800 transition-colors"
          title="Cerrar pestaña"
        >
          <span className="material-symbols-outlined text-[20px]">close</span>
          <span className="hidden sm:inline">Cerrar</span>
        </button>
      </div>
    </header>
  );
}
