import { expect, test } from '@playwright/test';

let cleanupState = {
    accountName: null,
    previousBudget: null,
    budgetSnapshotTaken: false,
};

test.afterEach(async ({ request }) => {
    if (!cleanupState.accountName) return;

    const accountsResponse = await request.get(
        '/api/accounts?includeArchived=true'
    );
    if (!accountsResponse.ok()) {
        throw new Error('Could not load accounts for E2E cleanup.');
    }
    const accounts = await accountsResponse.json();
    const account = accounts.find(
        (item) => item.name === cleanupState.accountName
    );
    if (!account) {
        cleanupState = {
            accountName: null,
            previousBudget: null,
            budgetSnapshotTaken: false,
        };
        return;
    }

    const transactionsResponse = await request.get(
        '/api/transactions?accountId=' + account.id + '&page=1&pageSize=100'
    );
    if (!transactionsResponse.ok()) {
        throw new Error('Could not load transactions for E2E cleanup.');
    }
    const transactions = await transactionsResponse.json();
    for (const item of transactions.items.filter(
        (item) => item.isManual && item.payee === 'Household Market'
    )) {
        const response = await request.delete('/api/transactions/' + item.id);
        if (!response.ok()) {
            throw new Error(
                'Could not delete E2E transaction ' + item.id + '.'
            );
        }
    }

    const budgetsResponse = await request.get(
        '/api/plan/budgets?includeInactive=true'
    );
    if (!budgetsResponse.ok()) {
        throw new Error('Could not load budgets for E2E cleanup.');
    }
    const budgets = await budgetsResponse.json();
    const groceryBudget = budgets.find(
        (item) => item.categoryName === 'Food & Groceries'
    );
    if (cleanupState.budgetSnapshotTaken && cleanupState.previousBudget) {
        const response = await request.put('/api/plan/budgets', {
            data: {
                categoryId: cleanupState.previousBudget.categoryId,
                monthlyAmount: cleanupState.previousBudget.monthlyAmount,
                rolloverEnabled: cleanupState.previousBudget.rolloverEnabled,
            },
        });
        if (!response.ok()) {
            throw new Error('Could not restore the previous grocery budget.');
        }
    } else if (cleanupState.budgetSnapshotTaken && groceryBudget) {
        const response = await request.delete(
            '/api/plan/budgets/' + groceryBudget.id
        );
        if (!response.ok()) {
            throw new Error('Could not delete the E2E grocery budget.');
        }
    }

    const response = await request.put('/api/accounts/' + account.id, {
        data: {
            name: account.name,
            isShared: account.isShared,
            accountType: account.accountType,
            institution: account.institution,
            lastFour: account.lastFour,
            safeZoneAmount: account.safeZoneAmount,
            includeInSafeToSpend: account.includeInSafeToSpend,
            isArchived: true,
            primaryHolderName: account.primaryHolderName,
            secondaryHolderName: account.secondaryHolderName,
            creditLimit: account.creditLimit,
        },
    });
    if (!response.ok()) {
        throw new Error('Could not archive the E2E account.');
    }
    cleanupState = {
        accountName: null,
        previousBudget: null,
        budgetSnapshotTaken: false,
    };
});
test('completes and reloads a household planning workflow', async ({
    page,
    request,
}) => {
    const suffix = Date.now().toString();
    const householdName = `The Finova Household ${suffix}`;
    const accountName = `Household Current ${suffix}`;
    const today = new Date().toISOString().slice(0, 10);

    await page.goto('/');
    await expect(
        page.getByRole('heading', { name: 'Welcome to Finova.' })
    ).toBeVisible();

    await page.getByLabel('First name').fill('Taylor');
    await page.getByLabel('Last name').fill('Tester');
    await page.getByLabel('Household name').fill(householdName);
    await page.getByRole('button', { name: /Continue to Finova/ }).click();

    await expect(page.getByRole('heading', { name: 'Overview' })).toBeVisible();

    await page.getByRole('link', { name: 'Settings' }).click();
    await expect(
        page.getByRole('heading', { name: 'Settings & accounts' })
    ).toBeVisible();
    await page.getByRole('button', { name: 'Add account' }).click();

    const accountDialog = page.getByRole('dialog', { name: 'Add an account' });
    await accountDialog.getByLabel('Account name').fill(accountName);
    await accountDialog.getByLabel('Account ownership').selectOption('joint');
    await accountDialog
        .getByLabel('First account holder')
        .fill('Taylor Tester');
    await accountDialog
        .getByLabel('Second account holder')
        .fill('Jordan Tester');
    await accountDialog.getByLabel('Institution').fill('Finova Bank');
    await accountDialog.getByLabel('Last four digits').fill('1234');
    await accountDialog.getByLabel('Opening balance').fill('1000');
    await accountDialog.getByLabel('Opening date').fill(today);
    await accountDialog.getByRole('button', { name: 'Save account' }).click();

    cleanupState.accountName = accountName;
    await expect(page.getByText(accountName, { exact: true })).toBeVisible();

    await page
        .getByLabel('Primary')
        .getByRole('link', { name: 'Transactions' })
        .click();
    await expect(
        page.getByRole('heading', { name: 'Transactions' })
    ).toBeVisible();
    await page.getByRole('button', { name: 'Add transaction' }).click();

    const transactionDialog = page.getByRole('dialog', {
        name: 'Add transaction',
    });
    await transactionDialog.getByLabel('Date').fill(today);
    await transactionDialog.getByLabel('Account').selectOption({
        label: accountName,
    });
    await transactionDialog.getByLabel('Amount').fill('42.50');
    await transactionDialog.getByLabel('Payee').fill('Household Market');
    await transactionDialog.getByLabel('Memo').fill('Weekly groceries');
    await transactionDialog.getByLabel('Category').selectOption({
        label: 'Food & Groceries',
    });
    await transactionDialog
        .getByRole('button', { name: 'Add transaction' })
        .click();

    await expect(
        page.getByRole('row').filter({ hasText: 'Household Market' })
    ).toBeVisible();

    await page.getByRole('link', { name: 'Plan' }).click();
    await expect(page.getByRole('heading', { name: 'Plan' })).toBeVisible();
    await page.getByRole('button', { name: 'Set a budget' }).click();

    const budgetDialog = page.getByRole('dialog', {
        name: 'Set a monthly budget',
    });
    await budgetDialog.getByLabel('Category').selectOption({
        label: 'Food & Groceries',
    });
    const budgetsBeforeResponse = await request.get(
        '/api/plan/budgets?includeInactive=true'
    );
    if (!budgetsBeforeResponse.ok()) {
        throw new Error('Could not snapshot the grocery budget.');
    }
    cleanupState.previousBudget = (await budgetsBeforeResponse.json()).find(
        (item) => item.categoryName === 'Food & Groceries'
    );
    cleanupState.budgetSnapshotTaken = true;
    await budgetDialog.getByLabel('Monthly amount').fill('300');
    await budgetDialog.getByLabel('Roll unused money forward').check();
    await budgetDialog.getByRole('button', { name: 'Save budget' }).click();

    await expect(
        page.getByText('Food & Groceries', { exact: true })
    ).toBeVisible();
    await expect(page.getByText('£257.50 left', { exact: true })).toBeVisible();

    await page.reload();
    await expect(page.getByText(accountName, { exact: true })).toBeVisible();
    await page.getByRole('link', { name: 'Overview' }).click();
    await expect(page.getByText(householdName, { exact: true })).toBeVisible();
    await expect(page.getByText('£957.50', { exact: true })).toBeVisible();
    await page.getByRole('link', { name: 'Plan' }).click();
    await expect(page.getByText('£257.50 left', { exact: true })).toBeVisible();
});
