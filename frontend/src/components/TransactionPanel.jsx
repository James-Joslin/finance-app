import React from 'react';
import UploadForm from './UploadForm';
import TransactionReviewForm from './TransactionReviewForm';

export default function TransactionPanel() {
    return (
        <div>
            <h2 className="text-lg font-semibold mb-2">Import Transactions</h2>
            <UploadForm />
            <h2 className="text-lg font-semibold mb-2"></h2>
            <TransactionReviewForm />
        </div>
    );
}
