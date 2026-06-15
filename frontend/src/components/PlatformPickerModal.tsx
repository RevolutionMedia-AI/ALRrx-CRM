import { Pizza01Icon, MedicineBottle01Icon, Shield01Icon } from 'hugeicons-react';
import type { ComponentType, SVGProps } from 'react';

type Platform = 'slice' | 'altrx' | 'admin';

interface PlatformPickerModalProps {
  userEmail: string;
  isAdmin: boolean;
  onSelect: (platform: Platform) => void;
  onCancel?: () => void;
}

type HugeIconComponent = ComponentType<SVGProps<SVGSVGElement> & { size?: number; color?: string; strokeWidth?: number }>;

interface PlatformOption {
  id: Platform;
  label: string;
  Icon: HugeIconComponent;
  description: string;
  accent: string;
  iconColor: string;
  hover: string;
  adminOnly?: boolean;
}

const OPTIONS: PlatformOption[] = [
  {
    id: 'slice',
    label: 'Slice',
    Icon: Pizza01Icon,
    description: 'Pizza shop operations & call-center analytics',
    accent: 'border-amber-warmth/40 bg-amber-warmth/5',
    iconColor: '#F59E0B',
    hover: 'hover:border-amber-warmth hover:bg-amber-warmth/10',
  },
  {
    id: 'altrx',
    label: 'Altrx',
    Icon: MedicineBottle01Icon,
    description: 'Pharmacy sales reports & real-time dashboard',
    accent: 'border-emerald-signal/40 bg-emerald-signal/5',
    iconColor: '#10B981',
    hover: 'hover:border-emerald-signal hover:bg-emerald-signal/10',
  },
  {
    id: 'admin',
    label: 'Admin Panel',
    Icon: Shield01Icon,
    description: 'Manage users, roles and platform access',
    accent: 'border-electric-blue/40 bg-electric-blue/5',
    iconColor: '#3B82F6',
    hover: 'hover:border-electric-blue hover:bg-electric-blue/10',
    adminOnly: true,
  },
];

export default function PlatformPickerModal({
  userEmail,
  isAdmin,
  onSelect,
  onCancel,
}: PlatformPickerModalProps) {
  const visibleOptions = OPTIONS.filter((opt) => !opt.adminOnly || isAdmin);
  const gridCols = visibleOptions.length >= 3
    ? 'grid-cols-1 sm:grid-cols-2 lg:grid-cols-3'
    : 'grid-cols-1 sm:grid-cols-2';

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center px-4"
      role="dialog"
      aria-modal="true"
      aria-labelledby="platform-picker-title"
    >
      <div
        className="absolute inset-0 bg-primary/60 backdrop-blur-sm"
        onClick={onCancel}
        aria-hidden="true"
      />

      <div className="relative z-10 w-full max-w-3xl bg-pure-surface dark:bg-gray-900 diffused-shadow-lg border border-whisper-border dark:border-gray-700 rounded-xl overflow-hidden">
        <div className={`h-1 w-full ${isAdmin && visibleOptions.length === 3 ? 'bg-gradient-to-r from-amber-warmth via-emerald-signal to-electric-blue' : 'bg-gradient-to-r from-amber-warmth to-emerald-signal'}`} />

        <div className="p-8 md:p-10 text-center">
          <p className="text-metadata-mono uppercase tracking-widest text-electric-blue mb-2">
            Welcome back
          </p>
          <h2
            id="platform-picker-title"
            className="text-headline-lg text-primary dark:text-white mb-2"
          >
            Choose a platform
          </h2>
          <p className="text-secondary dark:text-gray-400 text-sm">
            Signed in as <span className="font-medium text-primary dark:text-gray-200">{userEmail}</span>
            {isAdmin && <span className="ml-2 text-electric-blue font-medium">· Admin</span>}
          </p>
        </div>

        <div className={`grid ${gridCols} gap-4 px-8 pb-8 md:px-10 md:pb-10`}>
          {visibleOptions.map((opt) => {
            const { Icon } = opt;
            return (
              <button
                key={opt.id}
                type="button"
                onClick={() => onSelect(opt.id)}
                className={`group flex flex-col items-center justify-center gap-3 p-8 rounded-xl border-2 bg-pure-surface dark:bg-gray-900 ${opt.accent} ${opt.hover} active:scale-[0.98] transition-all duration-200`}
              >
                <div className="transition-transform duration-200 group-hover:scale-110">
                  <Icon size={56} color={opt.iconColor} strokeWidth={1.5} />
                </div>
                <span className="text-2xl font-bold text-primary dark:text-white">
                  {opt.label}
                </span>
                <span className="text-xs text-secondary dark:text-gray-400 text-center max-w-[220px]">
                  {opt.description}
                </span>
              </button>
            );
          })}
        </div>

        {onCancel && (
          <div className="px-8 pb-6 md:px-10 md:pb-8 text-center">
            <button
              type="button"
              onClick={onCancel}
              className="text-metadata-mono text-muted-slate dark:text-gray-500 hover:text-secondary dark:hover:text-gray-300 transition-colors"
            >
              Sign out
            </button>
          </div>
        )}
      </div>
    </div>
  );
}
