import { expect, test } from '@playwright/test';

test('completes and reloads a household planning workflow', async ({
    page,
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
