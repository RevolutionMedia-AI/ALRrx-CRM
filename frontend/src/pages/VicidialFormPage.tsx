import { useEffect } from 'react';
import { useSearchParams } from 'react-router-dom';
import VFormHeader from '../components/vicidial-form/VFormHeader';
import VSaleForm from '../components/vicidial-form/VSaleForm';
import { useAuth } from '../context/AuthContext';

export default function VicidialFormPage() {
  const { token, user, loading } = useAuth();
  const [searchParams] = useSearchParams();
  const leadId = searchParams.get('lead_id');
  const salesRep = searchParams.get('salesRep') ?? searchParams.get('full_name') ?? '';

  useEffect(() => {
    const parts: string[] = [];
    if (salesRep) parts.push(salesRep);
    if (leadId) parts.push(`Lead #${leadId}`);
    if (parts.length === 0) parts.push('Manual entry');
    document.title = `ALTRX Sales Form — ${parts.join(' • ')}`;
  }, [leadId, salesRep]);

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
