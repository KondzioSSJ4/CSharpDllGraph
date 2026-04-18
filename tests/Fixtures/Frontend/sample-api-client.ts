// Constant fetch - GET by default
fetch('/api/users')

// Constant fetch with explicit POST method
fetch('/api/users', { method: 'POST', body: JSON.stringify(payload) })

// Dynamic fetch - template literal, confidence: medium
fetch(`/api/users/${userId}`)

// Dynamic fetch with explicit method
fetch(`/api/orders/${orderId}`, { method: 'PUT' })

// Axios examples - constant URL, confidence: high
axios.get('/api/products')
axios.post('/api/orders', data)
axios.put('/api/orders/123', updatedData)
axios.delete('/api/orders/123')
axios.patch('/api/orders/123', patch)

// Axios examples - template literal URL, confidence: medium
axios.get(`/api/items/${itemId}/details`)
axios.post(`/api/users/${userId}/orders`, order)
