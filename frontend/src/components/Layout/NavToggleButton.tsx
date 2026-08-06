import { useNavHidden } from '../../hooks/useNavHidden';

export default function NavToggleButton() {
  const [navHidden, toggleNav] = useNavHidden();
  return (
    <button
      type="button"
      onClick={toggleNav}
      className="fixed bottom-3 left-3 z-[80] flex h-9 w-9 items-center justify-center rounded-full border border-whisper-border bg-pure-surface/95 text-primary shadow-lg backdrop-blur transition-colors hover:border-electric-blue dark:border-gray-700 dark:bg-gray-900/95 dark:text-gray-100"
      title={navHidden ? 'Show navigation' : 'Hide navigation'}
      aria-label={navHidden ? 'Show navigation' : 'Hide navigation'}
    >
      <span className="material-symbols-outlined text-[20px]">
        {navHidden ? 'expand_more' : 'expand_less'}
      </span>
    </button>
  );
}
