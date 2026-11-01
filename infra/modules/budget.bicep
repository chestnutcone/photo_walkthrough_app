// Resource-group budget. The alert fires when actual spend reaches 2x the planned monthly budget; a forecast notice at
// 100% gives earlier warning. Notification-only: nothing is stopped automatically.
targetScope = 'resourceGroup'

param name string
@description('Planned monthly spend in USD.')
param monthlyBudget int
param contactEmails array
@description('First day of a month (yyyy-MM-01). Keep it fixed: changing it on an existing budget makes the deployment fail.')
param startDate string

resource budget 'Microsoft.Consumption/budgets@2023-05-01' = {
  name: name
  properties: {
    category: 'Cost'
    amount: monthlyBudget
    timeGrain: 'Monthly'
    timePeriod: { startDate: startDate }
    notifications: {
      forecastAtPlan: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 100
        thresholdType: 'Forecasted'
        contactEmails: contactEmails
      }
      actualAtDouble: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 200
        thresholdType: 'Actual'
        contactEmails: contactEmails
      }
    }
  }
}
