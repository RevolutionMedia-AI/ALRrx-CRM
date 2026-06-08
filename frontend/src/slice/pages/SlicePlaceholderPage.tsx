interface PlaceholderProps {
  title: string;
  description: string;
  icon: string;
}

export default function SlicePlaceholderPage({ title, description, icon }: PlaceholderProps) {
  return (
    <>
      <div className="flex flex-col md:flex-row md:items-end justify-between gap-4 shrink-0">
        <div>
          <h2 className="font-headline-lg text-3xl font-bold text-primary mb-1">{title}</h2>
          <p className="text-steel-secondary">{description}</p>
        </div>
      </div>
      <div className="flex-1 bg-surface border border-whisper-border rounded-xl shadow-sm p-12 flex flex-col items-center justify-center text-center">
        <span className="material-symbols-outlined text-5xl text-muted-slate/40 mb-3">{icon}</span>
        <p className="text-base font-semibold text-primary">Coming soon</p>
        <p className="text-sm text-muted-slate mt-1 max-w-md">
          Esta vista forma parte de Slice pero aún no está implementada en este pase.
          Por ahora solo Shop Overview está conectada al backend.
        </p>
      </div>
    </>
  );
}
