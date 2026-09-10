require 'minitest/autorun'
require_relative '../scripts/stripe-local'

class StripeLocalTest < Minitest::Test
  def valid
    "# no real credentials\nBilling__Enabled=false\nBilling__MonthlyYen=500\nBilling__PriceId=price_fixture\nBilling__SecretKey=rk_test_fixture\nBilling__WebhookSecret=\n"
  end
  def test_accepts_test_only_settings
    assert_equal '500', TaskBoardStripeLocal.parse(valid)['Billing__MonthlyYen']
  end
  def test_rejects_live_key_without_echoing_it
    error = assert_raises(TaskBoardStripeLocal::SafeError) { TaskBoardStripeLocal.parse(valid.sub('rk_test_fixture', 'rk_live_fixture')) }
    refute_includes error.message, 'rk_live_fixture'
  end
  def test_rejects_publishable_key
    assert_raises(TaskBoardStripeLocal::SafeError) { TaskBoardStripeLocal.parse(valid.sub('rk_test_fixture', 'pk_test_fixture')) }
  end
  def test_rejects_duplicate_settings
    assert_raises(TaskBoardStripeLocal::SafeError) { TaskBoardStripeLocal.parse(valid + "Billing__SecretKey=rk_test_other\n") }
  end
  def test_rejects_wrong_price_field
    assert_raises(TaskBoardStripeLocal::SafeError) { TaskBoardStripeLocal.parse(valid.sub('price_fixture', 'prod_fixture')) }
  end
  def test_rejects_unapproved_amount
    assert_raises(TaskBoardStripeLocal::SafeError) { TaskBoardStripeLocal.parse(valid.sub('MonthlyYen=500', 'MonthlyYen=980')) }
  end
  def test_rejects_extra_setting
    assert_raises(TaskBoardStripeLocal::SafeError) { TaskBoardStripeLocal.parse(valid + "Other=secret\n") }
  end
  def test_rejects_invalid_enabled_value
    assert_raises(TaskBoardStripeLocal::SafeError) { TaskBoardStripeLocal.parse(valid.sub('Enabled=false', 'Enabled=yes')) }
  end
  def challenge
    { 'browser_url' => 'https://dashboard.stripe.com/stripecli/device', 'verification_code' => 'ABCD-EFGH',
      'next_step' => 'stripe login --complete-device' }
  end
  def test_accepts_device_login
    assert_equal 'device', TaskBoardStripeLocal.login_challenge(challenge)[:flow]
  end
  def test_accepts_official_access_server
    data = challenge.merge('browser_url' => 'https://access.stripe.com/stripecli/verify')
    assert_equal 'device', TaskBoardStripeLocal.login_challenge(data)[:flow]
  end
  def test_accepts_legacy_login
    data = challenge.merge('next_step' => "stripe login --complete 'https://dashboard.stripe.com/stripecli/auth/fixture'")
    assert_equal 'legacy', TaskBoardStripeLocal.login_challenge(data)[:flow]
  end
  def test_rejects_non_stripe_login_url
    assert_raises(TaskBoardStripeLocal::SafeError) { TaskBoardStripeLocal.login_challenge(challenge.merge('browser_url' => 'https://example.com/')) }
  end
  def test_rejects_login_command_injection
    assert_raises(TaskBoardStripeLocal::SafeError) { TaskBoardStripeLocal.login_challenge(challenge.merge('next_step' => 'stripe login --complete-device ; something')) }
  end
  def test_rejects_non_stripe_poll_url
    data = challenge.merge('next_step' => "stripe login --complete 'https://example.com/poll'")
    assert_raises(TaskBoardStripeLocal::SafeError) { TaskBoardStripeLocal.login_challenge(data) }
  end
  def price
    { 'id' => 'price_fixture', 'livemode' => false, 'active' => true, 'currency' => 'jpy', 'unit_amount' => 500,
      'type' => 'recurring', 'recurring' => { 'interval' => 'month', 'interval_count' => 1, 'usage_type' => 'licensed' },
      'billing_scheme' => 'per_unit', 'transform_quantity' => nil }
  end
  def test_validates_500_yen_test_price
    assert TaskBoardStripeLocal.validate_price(price, TaskBoardStripeLocal.parse(valid))
  end
  def test_rejects_live_price
    assert_raises(TaskBoardStripeLocal::SafeError) { TaskBoardStripeLocal.validate_price(price.merge('livemode' => true), TaskBoardStripeLocal.parse(valid)) }
  end
  def test_rejects_wrong_amount
    assert_raises(TaskBoardStripeLocal::SafeError) { TaskBoardStripeLocal.validate_price(price.merge('unit_amount' => 980), TaskBoardStripeLocal.parse(valid)) }
  end
  def test_rejects_different_sandbox_price
    assert_raises(TaskBoardStripeLocal::SafeError) { TaskBoardStripeLocal.validate_price(price.merge('id' => 'price_other'), TaskBoardStripeLocal.parse(valid)) }
  end
  def test_selects_only_authorized_sandbox_rows
    listing = "Authorized contexts (3):\n  free acct_first sandbox active\n  Another acct_second sandbox\n  Live acct_live live\n"
    assert_equal %w[acct_first acct_second], TaskBoardStripeLocal.authorized_sandbox_ids(listing)
  end
  def test_deduplicates_sandbox_rows
    assert_equal ['acct_first'], TaskBoardStripeLocal.authorized_sandbox_ids("Name acct_first sandbox\nName acct_first sandbox\n")
  end
end
