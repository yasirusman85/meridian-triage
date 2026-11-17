import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { ContractorPage, contractorTokenFromPath } from './ContractorPage';
import { NumbersPage } from './NumbersPage';
import { QueuePage } from './QueuePage';
import './styles.css';

const root = document.getElementById('root');
if (!root) {
  throw new Error('Root element missing');
}

// The contractor's single-job link is /c/<token> (AD-11); everything else is the staff queue.
const contractorToken = contractorTokenFromPath(window.location.pathname);

createRoot(root).render(
  <StrictMode>
    {contractorToken !== null ? (
      <ContractorPage token={contractorToken} />
    ) : window.location.pathname === '/numbers' ? (
      <NumbersPage />
    ) : (
      <QueuePage />
    )}
  </StrictMode>,
);
