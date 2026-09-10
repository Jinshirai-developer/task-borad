# Local, test-only Stripe checks and webhook forwarding. Never prints credentials.
require 'json'
require 'net/http'
require 'openssl'
require 'open3'
require 'fileutils'
require 'shellwords'

module TaskBoardStripeLocal
  ROOT = File.expand_path('..', __dir__)
  SETTINGS = File.join(ROOT, '.local/stripe-test.env')
  CLI_DIR = File.join(ROOT, '.local/stripe-cli')
  LOGIN_STATE = File.join(CLI_DIR, 'login-pending.json')
  API_VERSION = '2026-08-26.dahlia'
  EVENTS = %w[checkout.session.completed checkout.session.expired customer.subscription.created
    customer.subscription.updated customer.subscription.deleted invoice.paid invoice.payment_failed
    invoice.payment_action_required].freeze
  class SafeError < StandardError; end

  def self.parse(content)
    values = {}
    content.each_line do |raw|
      line = raw.strip
      next if line.empty? || line.start_with?('#')
      name, value = line.split('=', 2)
      raise SafeError, 'Invalid or duplicate settings; values withheld' unless value &&
        %w[Billing__Enabled Billing__MonthlyYen Billing__PriceId Billing__SecretKey Billing__WebhookSecret].include?(name) && !values.key?(name)
      values[name] = value
    end
    raise SafeError, 'A restricted test key is required' unless values.fetch('Billing__SecretKey', '').match?(/\Ark_test_[A-Za-z0-9]+\z/)
    raise SafeError, 'Invalid price ID format' unless values.fetch('Billing__PriceId', '').match?(/\Aprice_[A-Za-z0-9]+\z/)
    raise SafeError, 'This local setup expects the approved JPY 500 plan' unless values['Billing__MonthlyYen'] == '500'
    raise SafeError, 'Invalid enabled setting' unless %w[true false].include?(values['Billing__Enabled'])
    values
  end

  def self.settings
    raise SafeError, 'Settings must be a private, owned regular file' unless File.file?(SETTINGS) && !File.symlink?(SETTINGS) &&
      File.stat(SETTINGS).uid == Process.uid && (File.stat(SETTINGS).mode & 0777) == 0600
    parse(File.read(SETTINGS, encoding: 'UTF-8'))
  end

  def self.price_check(values)
    uri = URI("https://api.stripe.com/v1/prices/#{values.fetch('Billing__PriceId')}")
    request = Net::HTTP::Get.new(uri)
    request['Authorization'] = "Bearer #{values.fetch('Billing__SecretKey')}"
    request['Stripe-Version'] = API_VERSION
    response = Net::HTTP.start(uri.hostname, uri.port, use_ssl: true, verify_mode: OpenSSL::SSL::VERIFY_PEER,
      open_timeout: 15, read_timeout: 20) { |http| http.request(request) }
    raise SafeError, "Stripe price read failed (HTTP #{response.code}); response withheld" unless response.code == '200'
    price = JSON.parse(response.body)
    validate_price(price, values)
    { stripe_read_only: true, http_status: 200, test_mode: true, monthly_yen: 500, price_matches: true }
  end

  def self.validate_price(price, values)
    valid = price['id'] == values['Billing__PriceId'] && price['livemode'] == false && price['active'] == true &&
      price['currency'] == 'jpy' && price['unit_amount'] == 500 && price['type'] == 'recurring' &&
      price.dig('recurring', 'interval') == 'month' && price.dig('recurring', 'interval_count') == 1 &&
      price.dig('recurring', 'usage_type') == 'licensed' && price['billing_scheme'] == 'per_unit' && price['transform_quantity'].nil?
    raise SafeError, 'Stripe price does not match the approved test plan' unless valid
    true
  end

  def self.cli_environment(values)
    config_dir = File.join(CLI_DIR, 'config')
    FileUtils.mkdir_p(config_dir, mode: 0700)
    { 'STRIPE_API_KEY' => values.fetch('Billing__SecretKey'), 'STRIPE_CLI_TELEMETRY_OPTOUT' => '1',
      'XDG_CONFIG_HOME' => config_dir }
  end

  def self.cli_command(*args)
    binary = File.join(CLI_DIR, 'stripe')
    raise SafeError, 'Install the verified project-local Stripe CLI first' unless File.executable?(binary)
    [binary, '--config', File.join(CLI_DIR, 'config/config.toml'), '--device-name', 'task-board-local', '--color', 'off', *args]
  end

  def self.save_webhook(secret, values)
    raise SafeError, 'Invalid signing secret; values withheld' unless secret.match?(/\Awhsec_[A-Za-z0-9]+\z/)
    old = values.fetch('Billing__WebhookSecret', '')
    raise SafeError, 'An existing different signing secret requires review' unless old.empty? || old == secret
    return if old == secret
    raise SafeError, 'Settings changed during setup; retry after reviewing edits' unless settings == values
    content = File.read(SETTINGS, encoding: 'UTF-8')
    raise SafeError, 'Expected one empty WebhookSecret line' unless content.scan(/^Billing__WebhookSecret=$/).size == 1
    # Patch travels only through the child process stdin; never through shell args or tool output.
    patch = "*** Begin Patch\n*** Update File: #{SETTINGS}\n@@\n-Billing__WebhookSecret=\n+Billing__WebhookSecret=#{secret}\n*** End Patch\n"
    _out, _err, result = Open3.capture3('apply_patch', stdin_data: patch)
    raise SafeError, 'Signing secret save failed; output withheld' unless result.success? && settings['Billing__WebhookSecret'] == secret
  end

  def self.login_environment(values)
    cli_environment(values).merge('STRIPE_API_KEY' => nil)
  end

  def self.check_login_price(values)
    stdout, _stderr, result = Open3.capture3(login_environment(values), *cli_command('get',
      "/v1/prices/#{values.fetch('Billing__PriceId')}", '--stripe-version', API_VERSION))
    raise SafeError, 'CLI cannot access the same test price; verify the selected Sandbox' unless result.success?
    price = JSON.parse(stdout)
    # The CLI can exit 0 even when Stripe returned an error object.
    raise SafeError, 'CLI cannot access the same test price; verify the selected Sandbox' if price.key?('error')
    validate_price(price, values)
  end

  def self.authorized_sandbox_ids(list)
    list.each_line.map { |line| line[/\b(acct_[A-Za-z0-9]+)\s+sandbox(?:\s|$)/, 1] }.compact.uniq
  end

  def self.select_sandbox(values)
    environment = login_environment(values)
    stdout, _stderr, listed = Open3.capture3(environment, *cli_command('login', 'list'))
    raise SafeError, 'Cannot list authorized Sandbox contexts' unless listed.success?
    ids = authorized_sandbox_ids(stdout)
    raise SafeError, 'No bounded list of authorized Sandboxes available' unless ids.size.between?(1, 10)
    previous = stdout.each_line.map { |line| line[/\b(acct_[A-Za-z0-9]+)\s+sandbox\s+.*active/, 1] }.compact.first
    raise SafeError, 'Expected an active test context before selection' unless ids.include?(previous)
    selected = false
    begin
      ids.each do |id|
        # OAuth uses its selected context; do not mix an explicit Stripe-Context header with it.
        _out, _err, switched = Open3.capture3(environment, *cli_command('switch', 'context', id))
        raise SafeError, 'Sandbox selection failed; output withheld' unless switched.success?
        output, _errors, result = Open3.capture3(environment, *cli_command('get',
          "/v1/prices/#{values.fetch('Billing__PriceId')}", '--stripe-version', API_VERSION))
        raise SafeError, 'Authorized Sandbox price read failed; values withheld' unless result.success?
        price = JSON.parse(output)
        next if price.dig('error', 'code') == 'resource_missing'
        raise SafeError, 'Authorized Sandbox access error; values withheld' if price.key?('error')
        validate_price(price, values)
        selected = true
        break
      end
      raise SafeError, 'No authorized Sandbox contains this test price' unless selected
    ensure
      Open3.capture3(environment, *cli_command('switch', 'context', previous)) unless selected
    end
    check_login_price(values)
    { selected_matching_sandbox: true, monthly_yen: 500, live_mode: false, secrets_withheld: true }
  end

  def self.login_challenge(data)
    browser = URI(data.fetch('browser_url'))
    next_step = Shellwords.split(data.fetch('next_step'))
    device_flow = next_step == ['stripe', 'login', '--complete-device']
    legacy_flow = next_step.length == 4 && next_step[0, 3] == ['stripe', 'login', '--complete']
    raise SafeError, 'Unexpected login response' unless browser.scheme == 'https' && %w[dashboard.stripe.com access.stripe.com].include?(browser.host) &&
      browser.userinfo.nil? && browser.port == 443 && (device_flow || legacy_flow)
    poll = legacy_flow ? URI(next_step[3]) : nil
    raise SafeError, 'Unexpected login poll destination' if poll && !(poll.scheme == 'https' && poll.host == 'dashboard.stripe.com' &&
      poll.userinfo.nil? && poll.port == 443 && poll.path.start_with?('/stripecli/'))
    code = data.fetch('verification_code')
    raise SafeError, 'Unexpected verification code' unless code.match?(/\A[A-Za-z0-9-]+\z/)
    { browser_url: browser.to_s, verification_code: code, flow: device_flow ? 'device' : 'legacy', poll_url: poll&.to_s }
  end

  def self.start_login(values)
    raise SafeError, 'A pending login already exists; review it before starting another' if File.exist?(LOGIN_STATE)
    stdout, _stderr, result = Open3.capture3(login_environment(values), *cli_command('login', '--non-interactive'))
    raise SafeError, 'CLI browser login could not start; raw output withheld' unless result.success?
    state = login_challenge(JSON.parse(stdout))
    patch = "*** Begin Patch\n*** Add File: #{LOGIN_STATE}\n+#{JSON.generate(state)}\n*** End Patch\n"
    _out, _err, saved = Open3.capture3('apply_patch', stdin_data: patch)
    raise SafeError, 'Could not preserve the login challenge; output withheld' unless saved.success?
    state.select { |key, _| [:browser_url, :verification_code].include?(key) }.merge(sandbox_only: true)
  end

  def self.complete_login(values)
    raise SafeError, 'Start the dedicated browser login first' unless File.file?(LOGIN_STATE) && !File.symlink?(LOGIN_STATE) &&
      (File.stat(LOGIN_STATE).mode & 0777) == 0600
    data = JSON.parse(File.read(LOGIN_STATE))
    if data['flow'] == 'device'
      command = cli_command('login', '--complete-device')
    elsif data['flow'] == 'legacy'
      poll = URI(data.fetch('poll_url'))
      raise SafeError, 'Unexpected login poll destination' unless poll.scheme == 'https' && poll.host == 'dashboard.stripe.com' &&
        poll.userinfo.nil? && poll.port == 443 && poll.path.start_with?('/stripecli/')
      command = cli_command('login', '--complete', poll.to_s)
    else
      raise SafeError, 'Unknown login continuation'
    end
    _stdout, _stderr, result = Open3.capture3(login_environment(values), *command)
    raise SafeError, 'Browser login was not completed; raw output withheld' unless result.success?
    check_login_price(values)
    { cli_login_complete: true, same_test_price_accessible: true, secrets_withheld: true }
  end

  def self.prepare_webhook(values, with_login: false)
    check_login_price(values) if with_login
    environment = with_login ? login_environment(values) : cli_environment(values)
    stdout, stderr, result = Open3.capture3(environment, *cli_command('listen', '--skip-update',
      '--events', EVENTS.join(','), '--print-secret'))
    unless result.success?
      permission = (stdout + stderr).match?(/permission|403|unauthorized/i)
      raise SafeError, permission ? 'Stripe CLI permission denied; raw output withheld' : 'Stripe CLI setup failed; raw output withheld'
    end
    secret = stdout.strip
    save_webhook(secret, values)
    { webhook_secret_saved: true, secrets_withheld: true, billing_enabled: values['Billing__Enabled'] == 'true' }
  end

  def self.listen(values, with_login: false)
    expected = values.fetch('Billing__WebhookSecret', '')
    raise SafeError, 'Prepare the signing secret first' unless expected.match?(/\Awhsec_[A-Za-z0-9]+\z/)
    check_login_price(values) if with_login
    environment = with_login ? login_environment(values) : cli_environment(values)
    Open3.popen2e(environment, *cli_command('listen', '--skip-update', '--events', EVENTS.join(','),
      '--forward-to', 'http://127.0.0.1:5097/api/billing/stripe-webhook')) do |stdin, output, process|
      stdin.close
      begin
        output.each_line do |line|
          if line.include?('whsec_')
            secret = line[/whsec_[A-Za-z0-9]+/]
            raise SafeError, 'Listener signing secret differs; refusing forwarding' unless secret == expected
            puts JSON.generate(listener_ready: true, secrets_withheld: true)
          end
          if (version = line[/\d{4}-\d{2}-\d{2}\.[a-z]+/])
            raise SafeError, 'Sandbox API version differs from application SDK; review required' unless version == API_VERSION
            puts JSON.generate(api_version_matches: true)
          end
          if (status = line[/\[([1-5][0-9]{2})\]/, 1])
            puts JSON.generate(webhook_response_status: status.to_i)
          end
        end
        raise SafeError, 'Listener stopped unexpectedly; raw output withheld' unless process.value.success?
      ensure
        if process.alive?
          Process.kill('TERM', process.pid)
          process.join(5)
        end
      end
    end
  end

  def self.main(action)
    $stdout.sync = true
    File.umask(0077)
    values = settings
    case action
    when 'check' then puts JSON.generate(price_check(values))
    when 'prepare-webhook'
      puts JSON.generate(price_check(values))
      puts JSON.generate(prepare_webhook(values))
    when 'listen' then listen(values)
    when 'login' then puts JSON.generate(start_login(values))
    when 'complete-login' then puts JSON.generate(complete_login(values))
    when 'select-sandbox' then puts JSON.generate(select_sandbox(values))
    when 'prepare-webhook-login'
      puts JSON.generate(price_check(values))
      puts JSON.generate(prepare_webhook(values, with_login: true))
    when 'listen-login' then listen(values, with_login: true)
    else raise SafeError, 'Use: ruby scripts/stripe-local.rb check|prepare-webhook|listen|login|complete-login|select-sandbox|prepare-webhook-login|listen-login'
    end
  end
end

if __FILE__ == $PROGRAM_NAME
  begin
    TaskBoardStripeLocal.main(ARGV.fetch(0, ''))
  rescue TaskBoardStripeLocal::SafeError => error
    warn error.message
    exit 1
  rescue SocketError, SystemCallError, Timeout::Error, OpenSSL::SSL::SSLError, Net::OpenTimeout, Net::ReadTimeout
    warn 'Network or filesystem operation failed; details withheld'
    exit 2
  rescue StandardError
    warn 'Local Stripe operation failed; details withheld'
    exit 3
  end
end
