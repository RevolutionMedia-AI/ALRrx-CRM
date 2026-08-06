import { useNavHidden } from '../../hooks/useNavHidden';

interface NavToggleButtonProps {
  variant?: 'floating' | 'inline';
}

export default function NavToggleButton({ variant = 'floating' }: NavToggleButtonProps) {
  const [navHidden, toggleNav] = useNavHidden();
  const className =
    variant === 'inline'
      ? 'flex h-7 w-7 items-center justify-center rounded-md border border-whisper-border bg-surface-container-low text-secondary hover:text-primary hover:border-electric-blue transition-all dark:border-gray-700 dark:bg-gray-800 dark:text-gray-300 dark:hover:text-gray-100'
      : 'fixed bottom-3 left-3 z-[80] flex h-9 w-9 items-center justify-center rounded-full border border-whisper-border bg-pure-surface/95 text-primary shadow-lg backdrop-blur transition-colors hover:border-electric-blue dark:border-gray-700 dark:bg-gray-900/95 dark:text-gray-100';
  return (
    <button
      type="button"
      onClick={toggleNav}
      className={className}
      title={navHidden ? 'Show navigation' : 'Hide navigation'}
      aria-label={navHidden ? 'Show navigation' : 'Hide navigation'}
    >
      <span className="material-symbols-outlined text-[18px]">
        {navHidden ? 'expand_more' : 'expand_less'}
      </span>
    </button>
  );
}
