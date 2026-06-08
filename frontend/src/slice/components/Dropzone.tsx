import { useState, useCallback, type DragEvent, type ChangeEvent } from 'react';

interface DropzoneProps {
  accept: string;
  multiple?: boolean;
  maxSizeMB: number;
  disabled?: boolean;
  onFiles: (files: File[]) => void;
}

function formatBytes(b: number): string {
  if (b < 1024) return `${b} B`;
  if (b < 1024 * 1024) return `${(b / 1024).toFixed(1)} KB`;
  return `${(b / 1024 / 1024).toFixed(1)} MB`;
}

export default function Dropzone({ accept, multiple = false, maxSizeMB, disabled, onFiles }: DropzoneProps) {
  const [isDragActive, setIsDragActive] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [selected, setSelected] = useState<File[]>([]);

  const acceptList = accept.split(',').map((s) => s.trim().toLowerCase());

  const validate = useCallback(
    (files: FileList | File[]): File[] => {
      const arr = Array.from(files);
      const valid: File[] = [];
      for (const f of arr) {
        const ext = '.' + f.name.split('.').pop()?.toLowerCase();
        if (!acceptList.includes(ext)) {
          setError(`File "${f.name}" has unsupported extension ${ext}.`);
          return [];
        }
        if (f.size > maxSizeMB * 1024 * 1024) {
          setError(`File "${f.name}" exceeds the ${maxSizeMB} MB limit.`);
          return [];
        }
        valid.push(f);
      }
      setError(null);
      return valid;
    },
    [acceptList, maxSizeMB]
  );

  const handleDrop = (e: DragEvent<HTMLDivElement>) => {
    e.preventDefault();
    e.stopPropagation();
    setIsDragActive(false);
    if (disabled) return;
    const valid = validate(e.dataTransfer.files);
    if (valid.length === 0) return;
    const next = multiple ? [...selected, ...valid] : valid.slice(0, 1);
    setSelected(next);
    onFiles(next);
  };

  const handleSelect = (e: ChangeEvent<HTMLInputElement>) => {
    if (!e.target.files) return;
    const valid = validate(e.target.files);
    if (valid.length === 0) return;
    const next = multiple ? [...selected, ...valid] : valid.slice(0, 1);
    setSelected(next);
    onFiles(next);
    e.target.value = '';
  };

  const handleRemove = (idx: number) => {
    const next = selected.filter((_, i) => i !== idx);
    setSelected(next);
    onFiles(next);
  };

  return (
    <div className="flex flex-col gap-3">
      <div
        onDragEnter={(e) => {
          e.preventDefault();
          if (!disabled) setIsDragActive(true);
        }}
        onDragOver={(e) => {
          e.preventDefault();
          if (!disabled) setIsDragActive(true);
        }}
        onDragLeave={(e) => {
          e.preventDefault();
          setIsDragActive(false);
        }}
        onDrop={handleDrop}
        className={`flex-1 border-2 border-dashed rounded-lg flex flex-col items-center justify-center p-8 transition-colors cursor-pointer group ${
          isDragActive
            ? 'border-primary bg-surface-container-low'
            : 'border-outline-variant hover:bg-surface-container-low'
        } ${disabled ? 'opacity-50 pointer-events-none' : ''}`}
      >
        <div className="flex gap-4 mb-4 text-outline group-hover:text-primary transition-colors">
          <span className="material-symbols-outlined text-4xl">folder_zip</span>
          <span className="material-symbols-outlined text-4xl">description</span>
        </div>
        <p className="font-headline-lg text-[1.5rem] text-primary mb-2 text-center">
          {multiple ? 'Drag & Drop Excel files' : 'Drag & Drop .zip payload'}
        </p>
        <p className="text-secondary text-center mb-6 text-sm">or click to browse local directory</p>
        <div className="font-metadata-mono text-xs text-outline bg-surface-container-low px-3 py-1 rounded">
          MAX_FILE_SIZE: {maxSizeMB}MB | ALLOWED: {accept.split(',').map((s) => s.trim().toUpperCase()).join(', ')}
        </div>
        <input
          type="file"
          accept={accept}
          multiple={multiple}
          onChange={handleSelect}
          disabled={disabled}
          className="absolute inset-0 opacity-0 cursor-pointer"
          style={{ position: 'static', width: 0, height: 0, padding: 0, border: 0 }}
        />
        <button
          type="button"
          onClick={() => (document.getElementById('hidden-file-input') as HTMLInputElement | null)?.click()}
          disabled={disabled}
          className="mt-4 px-4 py-2 bg-surface border border-whisper-border text-primary text-sm font-semibold rounded hover:bg-surface-container transition-colors"
        >
          Browse files
        </button>
        <input
          id="hidden-file-input"
          type="file"
          accept={accept}
          multiple={multiple}
          onChange={handleSelect}
          disabled={disabled}
          className="hidden"
        />
      </div>

      {error && (
        <div className="bg-deep-rose/10 border border-deep-rose/20 rounded-lg px-3 py-2 text-deep-rose text-xs flex items-center gap-2">
          <span className="material-symbols-outlined text-sm">error</span>
          {error}
        </div>
      )}

      {selected.length > 0 && (
        <ul className="space-y-1.5">
          {selected.map((f, i) => (
            <li
              key={i}
              className="flex items-center justify-between bg-surface-container-low border border-whisper-border rounded-lg px-3 py-2 text-xs"
            >
              <div className="flex items-center gap-2 min-w-0">
                <span className="material-symbols-outlined text-secondary text-base">
                  {f.name.endsWith('.zip') ? 'folder_zip' : 'description'}
                </span>
                <span className="font-metadata-mono text-primary truncate">{f.name}</span>
                <span className="text-muted-slate shrink-0">{formatBytes(f.size)}</span>
              </div>
              <button
                onClick={() => handleRemove(i)}
                className="text-muted-slate hover:text-deep-rose transition-colors shrink-0"
                title="Remove"
              >
                <span className="material-symbols-outlined text-base">close</span>
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
