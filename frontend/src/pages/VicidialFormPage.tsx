import { useEffect, useState } from 'react';
import VFormHeader from '../components/vicidial-form/VFormHeader';
import VSaleForm from '../components/vicidial-form/VSaleForm';
import VSalesList from '../components/vicidial-form/VSalesList';

const SALES_REP_STORAGE_KEY = 'vicidial_form_sales_rep';

export default function VicidialFormPage() {
  const [refreshKey, setRefreshKey] = useState(0);
  const [salesRep, setSalesRep] = useState(() => {
    return sessionStorage.getItem(SALES_REP_STORAGE_KEY) ?? '';
  });

  useEffect(() => {
    if (salesRep) {
      sessionStorage.setItem(SALES_REP_STORAGE_KEY, salesRep);
    }
  }, [salesRep]);

  useEffect(() => {
    document.title = 'ALTRX Sales Form';
  }, []);

  const handleClose = () => {
    window.close();
  };

  const handleRefresh = () => {
    setRefreshKey((k) => k + 1);
  };

  return (
    <div className="bg-canvas-white dark:bg-gray-950 min-h-screen text-on-surface dark:text-gray-100 transition-colors">
      <VFormHeader onClose={handleClose} />
      <main className="max-w-3xl mx-auto px-4 sm:px-6 py-8">
        <div className="space-y-6">
          <VSaleForm
            salesRep={salesRep}
            onSalesRepChange={setSalesRep}
            onSubmitted={handleRefresh}
          />
          <VSalesList salesRep={salesRep} refreshKey={refreshKey} />
        </div>
      </main>
    </div>
  );
}
