import { useEffect } from 'react';
import VFormHeader from '../components/vicidial-form/VFormHeader';
import VSaleForm from '../components/vicidial-form/VSaleForm';
import { useAuth } from '../context/AuthContext';

export default function VicidialFormPage() {
  const { token, user, loading } = useAuth();

  useEffect(() => {
    document.title = 'ALTRX Sales Form';
  }, []);

  const isAuthenticated = !loading && !!token && !!user;

  return (
    <div className="bg-canvas-white dark:bg-gray-950 min-h-screen text-on-surface dark:text-gray-100 transition-colors">
      <VFormHeader showHomeButton={isAuthenticated} />
      <main className="max-w-3xl mx-auto px-4 sm:px-6 py-8">
        <VSaleForm />
      </main>
    </div>
  );
}
