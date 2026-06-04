import { useState } from 'react';
import VFormHeader from '../components/vicidial-form/VFormHeader';
import VSaleForm from '../components/vicidial-form/VSaleForm';
import VicidialSalesSection from '../components/vicidial-form/VicidialSalesSection';
import { useAuth } from '../context/AuthContext';

type Tab = 'submit' | 'sales';

export default function VicidialFormPage() {
  const { token, user, loading } = useAuth();
  const [tab, setTab] = useState<Tab>('submit');
  const [refreshKey, setRefreshKey] = useState(0);

  const isAuthenticated = !loading && !!token && !!user;

  return (
    <div className="bg-canvas-white dark:bg-gray-950 min-h-screen text-on-surface dark:text-gray-100 transition-colors">
      <VFormHeader showHomeButton={isAuthenticated} />
      <main className="max-w-7xl mx-auto px-4 sm:px-6 py-8">
        <div className="mb-6 flex items-center gap-2 border-b border-gray-200 dark:border-gray-700">
          <button
            type="button"
            onClick={() => setTab('submit')}
            className={`px-4 py-2 text-sm font-semibold border-b-2 -mb-px transition-colors ${
              tab === 'submit'
                ? 'border-blue-500 text-blue-600 dark:text-blue-400'
                : 'border-transparent text-gray-500 dark:text-gray-400 hover:text-gray-700 dark:hover:text-gray-200'
            }`}
          >
            New Sale
          </button>
          <button
            type="button"
            onClick={() => {
              setTab('sales');
              setRefreshKey((k) => k + 1);
            }}
            className={`px-4 py-2 text-sm font-semibold border-b-2 -mb-px transition-colors ${
              tab === 'sales'
                ? 'border-blue-500 text-blue-600 dark:text-blue-400'
                : 'border-transparent text-gray-500 dark:text-gray-400 hover:text-gray-700 dark:hover:text-gray-200'
            }`}
          >
            Sales Log
          </button>
        </div>

        {tab === 'submit' ? (
          <div className="max-w-3xl mx-auto">
            <VSaleForm />
          </div>
        ) : (
          <VicidialSalesSection refreshKey={refreshKey} />
        )}
      </main>
    </div>
  );
}
